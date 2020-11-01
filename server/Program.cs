using System;
using OmniSharp.Extensions.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using MediatR;
using System.Threading;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using System.Linq;
using System.Collections.Immutable;
using parser;
using System.IO;
using System.IO.Pipes;
using System.IO.Pipelines;
using Nerdbank.Streams;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Bogus;
using System.Security.Cryptography;
using System.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Document.Proposals;
using OmniSharp.Extensions.LanguageServer.Protocol.Models.Proposals;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace.Proposals;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System.Buffers;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace server
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            var (input, output) = await CreateNamedPipe();
            var server = await LanguageServer.From(options => ConfigureServer(options, input, output));
            await server.WaitForExit;
        }

        public static void ConfigureServer(LanguageServerOptions options, PipeReader input, PipeWriter output)
        {
            options
                .WithInput(input)
                .WithOutput(output)
                .ConfigureLogging(
                    x => x
                        .ClearProviders()
                        .AddLanguageProtocolLogging()
                        .SetMinimumLevel(LogLevel.Debug)
                )
                .WithServices(services =>
                {
                    services.AddSingleton<CompletionProvider>();
                    services.AddSingleton<HoverProvider>();
                    services.AddSingleton<TextDocumentStore>();
                    services.AddSingleton<TokenProvider>();
                    services.AddSingleton<OutlineProvider>();
                    services.AddSingleton<CodeActionProvider>();
                    services.AddSingleton<CodeActionProvider.CommandHandler>();
                    services.AddSingleton<CodeLensProvider>();
                    services.AddSingleton<FoldingRangeProvider>();
                    services.AddSingleton<SelectionRangeProvider>();
                    services
                        .ConfigureSection<IniConfiguration>("ini")
                        .ConfigureSection<NinConfiguration>("nin");
                })
                .WithConfigurationSection("ini")
                .WithConfigurationSection("nin");
        }

        private static async Task<(PipeReader input, PipeWriter output)> CreateNamedPipe()
        {

            var pipe = new NamedPipeServerStream(
                            pipeName: @"ninrocks",
                            direction: PipeDirection.InOut,
                            maxNumberOfServerInstances: 1,
                            transmissionMode: PipeTransmissionMode.Byte,
                            options: System.IO.Pipes.PipeOptions.CurrentUserOnly | System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync();
            var pipeline = pipe.UsePipe();
            // await pipe.WaitForConnectionAsync().ConfigureAwait(false);
            return (pipeline.Input, pipeline.Output);
        }

        public static IServiceCollection ConfigureSection<TOptions>(this IServiceCollection services, string? sectionName)
            where TOptions : class
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOptions();
            services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(
                _ => new ConfigurationChangeTokenSource<TOptions>(
                    Options.DefaultName,
                    sectionName == null ? _.GetRequiredService<IConfiguration>() : _.GetRequiredService<IConfiguration>().GetSection(sectionName)
                )
            );
            return services.AddSingleton<IConfigureOptions<TOptions>>(
                _ => new NamedConfigureFromConfigurationOptions<TOptions>(
                    Options.DefaultName,
                    sectionName == null ? _.GetRequiredService<IConfiguration>() : _.GetRequiredService<IConfiguration>().GetSection(sectionName)
                )
            );
        }
    }

    class SelectionRangeProvider : SelectionRangeHandler
    {

        private readonly TextDocumentStore store;

        public SelectionRangeProvider(TextDocumentStore store) : base(new SelectionRangeRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector
        })
        {
            this.store = store;
        }

        public override async Task<Container<SelectionRange>> Handle(SelectionRangeParams request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!store.TryGetDocument(request.TextDocument.Uri, out var document)) return null;

            var results = new List<SelectionRange>();
            foreach (var position in request.Positions)
            {

                var range = document.GetItemAtPosition(position) switch
                {
                    NinValue v => GetSelectionRangeForValue(position, v, document),
                    NinSection s => new SelectionRange()
                    {
                        Range = s.Location,
                        Parent = new SelectionRange() { Range = ((s.Location.Start.Line, s.Location.Start.Character - 1), (s.Location.End.Line, s.Location.End.Character + 1)) }
                    },
                    _ => null
                };
                if (range == null) continue;
                results.Add(range);
            }

            return results;

            static SelectionRange GetSelectionRangeForValue(Position position, NinValue value, NinDocument document)
            {
                var ranges = new List<Range>();
                if (position >= value.ValueLocation.Start && position <= value.ValueLocation.End)
                {
                    ranges.Add(value.ValueLocation);
                }
                if (position >= value.KeyLocation.Start && position <= value.KeyLocation.End)
                {
                    ranges.Add(value.KeyLocation);
                }
                ranges.Add((value.KeyLocation.Start, value.ValueLocation.End));

                var section = document.GetSections().Single(z => z.Section == value.Section);

                var end = document.GetValues()
                    .Where(x => x.Section == value.Section)
                    .MaxBy(z => z.ValueLocation.End).FirstOrDefault();
                if (end != null)
                {
                    ranges.Add(((section.Location.Start.Line, section.Location.Start.Character - 1), end.ValueLocation.End));
                }

                ranges.Reverse();
                var result = ranges.Aggregate<Range, SelectionRange>(null, (acc, value) => new SelectionRange()
                {
                    Range = value,
                    Parent = acc
                });
                return result;
            }
        }
    }

    class FoldingRangeProvider : FoldingRangeHandler
    {

        private readonly TextDocumentStore store;

        public FoldingRangeProvider(TextDocumentStore store) : base(new FoldingRangeRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector
        })
        {
            this.store = store;
        }

        public override async Task<Container<FoldingRange>> Handle(FoldingRangeRequestParam request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!store.TryGetDocument(request.TextDocument.Uri, out var document)) return null;

            return document.GetSections()
                .Select(z =>
                {
                    var last = document.GetValues()
                    .Where(x => x.Section == z.Section)
                    .Aggregate(new Position(0, 0), (acc, v) =>
                    {

                        return acc > v.ValueLocation.End ? acc : v.ValueLocation.End;
                    });
                    return new FoldingRange()
                    {
                        StartLine = z.Location.Start.Line,
                        StartCharacter = z.Location.Start.Character,
                        EndLine = last.Line,
                        EndCharacter = last.Character,
                        Kind = FoldingRangeKind.Region
                    };
                }
                )
                .ToArray();
        }
    }

    class CodeLensData : HandlerIdentity
    {
        public DocumentUri Uri { get; set; }
        public string Section { get; set; }
    }

    class CodeLensProvider : CodeLensHandlerBase<CodeLensData>
    {
        private readonly TextDocumentStore store;

        public CodeLensProvider(TextDocumentStore store) : base(new CodeLensRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector,
            ResolveProvider = true
        })
        {
            this.store = store;
        }

        protected override async Task<CodeLensContainer<CodeLensData>> HandleParams(CodeLensParams request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!store.TryGetDocument(request.TextDocument.Uri, out var document)) return null;

            return document.GetSections().Select(z => new CodeLens<CodeLensData>()
            {
                Data = new CodeLensData() { Uri = request.TextDocument.Uri, Section = z.Section },
                Range = ((z.Location.Start.Line, z.Location.Start.Character - 1), (z.Location.End.Line, z.Location.End.Character + 1))
            }).ToArray();
        }

        protected override async Task<CodeLens<CodeLensData>> HandleResolve(CodeLens<CodeLensData> request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!store.TryGetDocument(request.Data.Uri, out var document)) return request;
            request.Command = new Command() { Title = $"🔑 {document.GetValues().Count(x => x.Section == request.Data.Section)} Values" };
            return request;
        }
    }

    class DataLocation : HandlerIdentity
    {
        public Location Location { get; set; }
    }

    class CodeActionProvider : CodeActionHandlerBase<DataLocation>
    {
        private readonly TextDocumentStore store;

        public CodeActionProvider(TextDocumentStore store) : base(new CodeActionRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector,
            ResolveProvider = true,
            CodeActionKinds = new Container<CodeActionKind>(
                CodeActionKind.Empty,
                CodeActionKind.QuickFix,
                CodeActionKind.Refactor,
                CodeActionKind.RefactorExtract,
                CodeActionKind.RefactorInline,
                CodeActionKind.RefactorRewrite,
                CodeActionKind.Source,
                CodeActionKind.SourceOrganizeImports
            )
        })
        {
            this.store = store;
        }

        protected override async Task<CommandOrCodeActionContainer> HandleParams(CodeActionParams request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!store.TryGetDocument(request.TextDocument.Uri, out var document)) return null;
            var item = document.GetItemAtPosition(request.Range.Start);
            if (!(item is NinValue value)) return null;

            if (
                request.Range.Start >= value.ValueLocation.Start && request.Range.Start <= value.ValueLocation.End
                 || request.Range.End >= value.ValueLocation.Start && request.Range.End <= value.ValueLocation.End
            )
            {
                if (value.Value.AsSpan().Slice(0, 1).IsWhiteSpace())
                {
                    return new CommandOrCodeActionContainer(new CommandOrCodeAction(new CodeAction<Data>()
                    {
                        Title = "Remove Whitespace",
                        Kind = CodeActionKind.QuickFix,
                        Command = Command.Create("fix-whitespace")
                            .WithArguments(new DataLocation()
                            {
                                Location = new Location()
                                {
                                    Range = request.Range,
                                    Uri = request.TextDocument.Uri
                                }
                            }),
                    }));
                }
            }
            return null;
        }

        protected override Task<CodeAction<DataLocation>> HandleResolve(CodeAction<DataLocation> request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request);
        }

        public class CommandHandler : ExecuteCommandHandlerBase<DataLocation>
        {
            private readonly TextDocumentStore store;
            private readonly IWorkspaceLanguageServer languageServer;

            public CommandHandler(TextDocumentStore store, ISerializer serializer, IWorkspaceLanguageServer languageServer) : base("fix-whitespace", serializer)
            {
                this.store = store;
                this.languageServer = languageServer;
            }

            public override async Task<Unit> Handle(DataLocation arg1, CancellationToken cancellationToken)
            {
                await Task.Yield();
                if (!store.TryGetDocument(arg1.Location.Uri, out var document)) return Unit.Value;
                var item = document.GetItemAtPosition(arg1.Location.Range.Start);
                if (!(item is NinValue value)) return Unit.Value;

                await languageServer.ApplyWorkspaceEdit(new ApplyWorkspaceEditParams()
                {
                    Label = "Fixing whitespace",
                    Edit = new WorkspaceEdit()
                    {
                        DocumentChanges = new Container<WorkspaceEditDocumentChange>(
                            new TextDocumentEdit()
                            {
                                TextDocument = new VersionedTextDocumentIdentifier()
                                {
                                    Uri = arg1.Location.Uri,
                                    Version = document.Version
                                },
                                Edits = new TextEditContainer(
                                    new TextEdit()
                                    {
                                        NewText = "",
                                        Range = (
                                            (value.ValueLocation.Start.Line, value.ValueLocation.Start.Character),
                                            (value.ValueLocation.Start.Line, value.ValueLocation.Start.Character + (value.Value.Length - value.Value.Trim().Length))
                                        )
                                    }
                                )
                            }
                        )
                    }
                }, cancellationToken: cancellationToken);

                return Unit.Value;
            }
        }
    }

    class OutlineProvider : DocumentSymbolHandler
    {
        private readonly TextDocumentStore store;

        public OutlineProvider(TextDocumentStore store) : base(new DocumentSymbolRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector,
            Label = "NIN"
        })
        {
            this.store = store;
        }

        public override async Task<SymbolInformationOrDocumentSymbolContainer> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!store.TryGetDocument(request.TextDocument.Uri, out var document)) return null;

            return document.GetSections()
                .Select(section =>
                {
                    var children = document.GetValues()
                        .Where(z => z.Section == section.Section)
                        .ToArray();
                    return new DocumentSymbol()
                    {
                        Name = section.Section,
                        Kind = SymbolKind.Namespace,
                        Range = (section.Location.Start, children.OrderByDescending(z => z.ValueLocation.End.Line).ThenBy(z => z.ValueLocation.End.Character).First().ValueLocation.End),
                        SelectionRange = section.Location,
                        Children = document.GetValues()
                                            .Where(z => z.Section == section.Section)
                                            .Select(value => new DocumentSymbol()
                                            {
                                                Name = value.Key,
                                                Kind = SymbolKind.Key,
                                                Range = (value.KeyLocation.Start, value.ValueLocation.End),
                                                SelectionRange = value.KeyLocation,
                                                Children = new Container<DocumentSymbol>(
                                                        new DocumentSymbol()
                                                        {
                                                            Name = value.Value,
                                                            Kind = SymbolKind.Property,
                                                            Range = value.ValueLocation,
                                                            SelectionRange = value.ValueLocation
                                                        }).ToArray()
                                            })
                                            .ToArray()
                    };
                })
                .ToArray();
        }
    }

    class HighlightProvider : DocumentHighlightHandler
    {
        private readonly TextDocumentStore store;

        public HighlightProvider(TextDocumentStore store) : base(new DocumentHighlightRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector,
        })
        {
            this.store = store;
        }

        public override async Task<DocumentHighlightContainer> Handle(DocumentHighlightParams request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!store.TryGetDocument(request.TextDocument.Uri, out var document)) return null;


            return document.GetValues()
                    .SelectMany(z => new[] {
                        new DocumentHighlight() {
                            Kind = DocumentHighlightKind.Text,
                            Range = z.KeyLocation
                        },
                        new DocumentHighlight() {
                            Kind = DocumentHighlightKind.Text,
                            Range = z.ValueLocation
                        }
                    })
                    .Concat(document.GetSections().Select(z => new DocumentHighlight()
                    {
                        Kind = DocumentHighlightKind.Text,
                        Range = z.Location
                    }))
                .ToArray();
        }
    }

    class IniConfiguration
    {
        public bool Rainbow { get; set; }
    }

    class NinConfiguration
    {
        public bool Hardrock { get; set; }
    }

    class TokenProvider : SemanticTokensHandlerBase
    {
        private readonly TextDocumentStore store;
        private readonly IOptionsMonitor<IniConfiguration> optionsMonitor;

        public TokenProvider(TextDocumentStore store, IOptionsMonitor<IniConfiguration> optionsMonitor, IWorkspaceLanguageServer textDocumentLanguageServer) : base(new SemanticTokensRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector,
            Full = new SemanticTokensCapabilityRequestFull()
            {
                Delta = true
            },
            Range = new SemanticTokensCapabilityRequestRange() { },
            Legend = new SemanticTokensLegend()
        })
        {
            this.store = store;
            this.optionsMonitor = optionsMonitor;
            var currentState = optionsMonitor.CurrentValue.Rainbow;
            optionsMonitor.OnChange(config =>
            {
                if (config.Rainbow != currentState)
                {
                    textDocumentLanguageServer.RequestSemanticTokensRefresh(new SemanticTokensRefreshParams());
                    currentState = config.Rainbow;
                }
            });
        }

        protected override async Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
        {

            await Task.Yield();
            if (!store.TryGetTokenDocument(@params.TextDocument.Uri, GetRegistrationOptions(), out var document))
            {
                return new SemanticTokensDocument(GetRegistrationOptions());
            }
            return document;
        }

        Func<string, (SemanticTokenModifier modifier, SemanticTokenType type)> TokenizeValues(SemanticTokensLegend legend)
        {
            (SemanticTokenModifier modifier, SemanticTokenType type) GetSemanticTokens(string value)
            {
                var seed = Math.Abs(BitConverter.ToInt32(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(value)).AsSpan()[0..4]));
                var modifiers = legend.TokenModifiers.Select(z => (SemanticTokenModifier)z).ToArray();
                var types = legend.TokenTypes.Select(z => (SemanticTokenType)z).ToArray();

                return (modifiers.ElementAt(seed % modifiers.Length), types.ElementAt(seed % types.Length));
            }

            return value => GetSemanticTokens(value);
        }

        Func<SemanticTokenType> TokenizeRainbow(SemanticTokensLegend legend)
        {

            // var modifiers = legend.TokenModifiers.Select(z => (SemanticTokenModifier)z).Repeat().GetEnumerator();
            var types = legend.TokenTypes
                .Select(z => (SemanticTokenType)z)
                .OrderBy(z => z.ToString())
                .Reverse()
                .Repeat()
                .GetEnumerator();

            IEnumerable<SemanticTokenType> GetSemanticTokens()
            {
                while (true)
                {
                    // modifiers.MoveNext();
                    types.MoveNext();
                    yield return types.Current;
                }
            }

            var enumerator = GetSemanticTokens().GetEnumerator();

            return () =>
            {
                enumerator.MoveNext();
                return enumerator.Current;
            };
        }

        protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
        {
            var legend = GetRegistrationOptions().Legend;
            if (!store.TryGetDocument(identifier.TextDocument.Uri, out var document)) return Task.CompletedTask;
            var text = document.GetText().AsSpan();

            var start = 0;

            void advanceBy(ref ReadOnlySpan<char> text, int count)
            {
                text = text[count..];
                start += count;
            }

            if (optionsMonitor.CurrentValue.Rainbow)
            {
                var rainbowTokenizer = TokenizeRainbow(legend);
                while (!text.IsEmpty)
                {
                    var sectionIndex = text.IndexOf('[');
                    var spaceIndex = text.IndexOfAny(' ', '=', '\n');

                    var first = Math.Min(sectionIndex, spaceIndex);
                    if (first > -1 || spaceIndex > -1 || sectionIndex > -1)
                    {
                        if (first == -1) first = Math.Max(spaceIndex, sectionIndex);
                        if (first == sectionIndex)
                        {
                            var endIndex = text.IndexOf(']');
                            if (endIndex > -1 && endIndex < text.IndexOf('\n'))
                            {
                                var type = rainbowTokenizer();
                                var pos = document.GetPositionAtIndex(start + first + 1);
                                builder.Push(pos.Line, pos.Character, endIndex - first - 1, type, SemanticTokenModifier.Static);
                                advanceBy(ref text, endIndex + 1);
                            }
                        }
                        else
                        {
                            var type = rainbowTokenizer();
                            var pos = document.GetPositionAtIndex(start);
                            builder.Push(pos.Line, pos.Character, first, type, SemanticTokenModifier.Static);
                            advanceBy(ref text, first + 1);

                            var next = text.IndexOfAny(' ', '=', '\n');
                            if (next == -1) break;
                            // advanceBy(ref text, next);

                            // var (modifier, type) = rainbowTokenizer();
                            // var pos = document.GetPositionAtIndex(start + first);
                            // builder.Push(pos.Line, pos.Character, next, type, modifier);

                            // advanceBy(ref text, next + 1);
                            continue;
                        }

                        var end = text.IndexOf('\n');
                        if (end == -1) break;

                        advanceBy(ref text, end + 1);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            var valueTokenizer = TokenizeValues(legend);

            while (!text.IsEmpty)
            {
                var sectionIndex = text.IndexOf('[');
                var valueIndex = text.IndexOf('=');
                var first = Math.Min(sectionIndex, valueIndex);
                if (first > -1 || valueIndex > -1 || sectionIndex > -1)
                {
                    if (first == -1) first = Math.Max(valueIndex, sectionIndex);
                    advanceBy(ref text, first);

                    if (first == sectionIndex)
                    {
                        //section
                        var item = document.GetItemAtIndex(start + sectionIndex + 1) as NinSection;
                        if (item != null)
                        {
                            builder.Push(item.Location.Start.Line, item.Location.Start.Character, item.Section.Length, SemanticTokenType.Interface, SemanticTokenModifier.Static, SemanticTokenModifier.Declaration);
                        }
                    }
                    else
                    {
                        //value
                        var item = document.GetItemAtIndex(start + valueIndex + 1) as NinValue;
                        if (item != null)
                        {
                            builder.Push(item.KeyLocation.Start.Line, item.KeyLocation.Start.Character, item.Key.Length, SemanticTokenType.Keyword, SemanticTokenModifier.Definition);
                        }

                        if (item?.Value.Length > 5)
                        {
                            var (modifier, type) = valueTokenizer(item.Value.Substring(0, 5));
                            builder.Push(item.ValueLocation.Start.Line, item.ValueLocation.Start.Character, item.Value.Length, type, modifier);
                        }
                    }

                    var end = text.IndexOf('\n');
                    if (end == -1) break;

                    advanceBy(ref text, end + 1);
                }
                else
                {
                    break;
                }
            }

            return Task.CompletedTask;
        }
    }

    class Data : HandlerIdentity
    {
        public string Section { get; set; }
        public string Key { get; set; }
        public int Seed { get; set; }
    }
    class CompletionProvider : CompletionHandlerBase<Data>
    {
        private readonly TextDocumentStore store;
        private readonly IOptionsMonitor<NinConfiguration> options;

        public CompletionProvider(TextDocumentStore store, IOptionsMonitor<NinConfiguration> options) : base(new CompletionRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector,
            ResolveProvider = true,
            TriggerCharacters = new Container<string>(new[] { "=", "[" }),
            AllCommitCharacters = new Container<string>(new[] { "\n" })
        })
        {
            this.store = store;
            this.options = options;
        }

        protected override async Task<CompletionList<Data>> HandleParams(CompletionParams request, CancellationToken cancellationToken)
        {
            if (options.CurrentValue.Hardrock) return new CompletionList<Data>();
            await Task.Yield();
            if (!store.TryGetDocument(request.TextDocument.Uri, out var document)) return null;
            var item = document.GetItemAtPosition(request.Position);
            if (item is null) return new CompletionList<Data>();

            using var hasher = MD5.Create();
            var data = new Data();
            var key = new StringBuilder();
            if (item is NinValue value)
            {
                key.Append(value.Section);
                key.Append(value.Key);
                data.Section = value.Section;
                data.Key = value.Key;
            }

            var hash = key.Append(request.TextDocument.Uri.ToUnencodedString());
            var seed = BitConverter.ToInt32(hasher.ComputeHash(Encoding.UTF8.GetBytes(key.ToString())).AsSpan()[0..4]);
            data.Seed = seed;

            var faker = new Faker<CompletionItem<Data>>()
                .UseSeed(seed)
                .RuleFor(z => z.Kind, z => z.PickRandom<CompletionItemKind>())
                .RuleFor(z => z.Deprecated, z => z.Random.Bool(0.2f))
                .RuleFor(z => z.Label, z => item is NinValue ? z.Commerce.ProductName() : z.Commerce.Department())
                .RuleFor(z => z.SortText, (_, c) => c.Label)
                .RuleFor(z => z.FilterText, (_, c) => c.Label)
                .RuleFor(z => z.InsertText, (_, c) => c.Label)
                .RuleFor(z => z.Data, (f, c) =>
                {
                    return new Data()
                    {
                        Section = data.Section,
                        Key = data.Key,
                        Seed = BitConverter.ToInt32(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(c.Label)).AsSpan()[0..4])
                    };
                });

            return new CompletionList<Data>(faker.Generate(50));

        }

        protected override Task<CompletionItem<Data>> HandleResolve(CompletionItem<Data> request, CancellationToken cancellationToken)
        {
            var faker = new Faker() { Random = new Randomizer(request.Data.Seed) };

            if (string.IsNullOrEmpty(request.Data.Key))
            {
                request.Detail = faker.Commerce.Ean13();
            }
            else
            {
                request.Detail = faker.Commerce.ProductAdjective() + " " + faker.Commerce.ProductMaterial();
            }
            request.Documentation = faker.Lorem.Paragraphs(2);
            return Task.FromResult(request);
        }
    }

    class HoverProvider : HoverHandler
    {
        private readonly TextDocumentStore store;

        public HoverProvider(TextDocumentStore store) : base(new HoverRegistrationOptions()
        {
            DocumentSelector = store.GetRegistrationOptions().DocumentSelector
        })
        {
            this.store = store;
        }

        public override async Task<Hover> Handle(HoverParams request, CancellationToken cancellationToken)
        {
            if (!store.TryGetDocument(request.TextDocument.Uri, out var document)) return null;
            var item = document.GetItemAtPosition(request.Position);
            return item switch
            {
                NinValue v => new Hover()
                {
                    Contents = new MarkedStringsOrMarkupContent(
                        new MarkedString($"[{v.Section}]"),
                        new MarkedString($"key: {v.Key}\n\nvalue: {v.Value}")
                    ),
                    Range = ((v.KeyLocation.Start.Line, v.KeyLocation.Start.Character), (v.KeyLocation.Start.Line, v.ValueLocation.Start.Character))
                },
                NinSection s => new Hover()
                {
                    Range = s.Location,
                    Contents = new MarkedStringsOrMarkupContent(new MarkedString($"section: {s.Section}"))
                },
                _ => null
            };
        }
    }

    class TextDocumentStore : ITextDocumentSyncHandler
    {
        private readonly TextDocumentChangeRegistrationOptions _options;
        private readonly TextDocumentSaveRegistrationOptions _saveOptions;
        private readonly ILanguageServerFacade languageServer;
        private SynchronizationCapability _capability;
        private ImmutableDictionary<DocumentUri, NinDocument> _openDocuments = ImmutableDictionary<DocumentUri, NinDocument>.Empty.WithComparers(DocumentUri.Comparer);
        private ImmutableDictionary<DocumentUri, SemanticTokensDocument> _tokenDocuments = ImmutableDictionary<DocumentUri, SemanticTokensDocument>.Empty.WithComparers(DocumentUri.Comparer);

        public TextDocumentStore(ILanguageServerFacade languageServer)
        {
            _options = new TextDocumentChangeRegistrationOptions()
            {
                DocumentSelector = new DocumentSelector(DocumentSelector.ForPattern("**/*.ini", "**/*.nin").Concat(DocumentSelector.ForScheme("ini"))),
                SyncKind = TextDocumentSyncKind.Incremental
            };
            _saveOptions = new TextDocumentSaveRegistrationOptions()
            {
                DocumentSelector = _options.DocumentSelector,
                IncludeText = true
            };
            this.languageServer = languageServer;
        }

        public NinDocument? GetDocument(DocumentUri documentUri)
        {
            return _openDocuments.TryGetValue(documentUri, out var value) ? value : null;
        }

        public bool TryGetDocument(DocumentUri documentUri, out NinDocument document)
        {
            return _openDocuments.TryGetValue(documentUri, out document);
        }

        public bool TryGetTokenDocument(DocumentUri documentUri, SemanticTokensRegistrationOptions options, out SemanticTokensDocument document)
        {
            if (_openDocuments.TryGetValue(documentUri, out _))
            {
                if (!_tokenDocuments.TryGetValue(documentUri, out document))
                    _tokenDocuments = _tokenDocuments.Add(documentUri, document = new SemanticTokensDocument(options));
                return true;
            }
            document = null;
            return false;
        }

        public TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        {
            if (uri.Path.EndsWith(".ini"))
            {
                return new TextDocumentAttributes(uri, "ini");
            }
            if (uri.Path.EndsWith(".nin"))
            {
                return new TextDocumentAttributes(uri, "ini");
            }
            return null;
        }

        public Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
        {
            if (!_openDocuments.TryGetValue(request.TextDocument.Uri, out var value)) return Unit.Task;
            var changes = request.ContentChanges.ToArray();
            // full text change;
            if (changes.Length == 1 && changes[0].Range == default)
            {
                value.Load(changes[0].Text);
            }
            else
            {
                value.Update(changes);
            }

            languageServer.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
            {
                Diagnostics = new Container<Diagnostic>(value.GetDiagnostics()),
                Uri = value.DocumentUri,
                Version = value.Version
            });
            return Unit.Task;
        }

        public Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
        {
            lock (_openDocuments)
            {
                var document = new NinDocument(request.TextDocument.Uri);
                _openDocuments = _openDocuments.Add(request.TextDocument.Uri, document);
                document.Load(request.TextDocument.Text);

                languageServer.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
                {
                    Diagnostics = new Container<Diagnostic>(document.GetDiagnostics()),
                    Uri = document.DocumentUri,
                    Version = document.Version
                });
            }

            return Unit.Task;
        }

        public Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
        {
            lock (_openDocuments)
            {
                _openDocuments = _openDocuments.Remove(request.TextDocument.Uri);
            }

            return Unit.Task;
        }

        public Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
        {
            if (!_capability.DidSave) return Unit.Task;
            if (_openDocuments.TryGetValue(request.TextDocument.Uri, out var value))
            {
                value.Load(request.Text);

                languageServer.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
                {
                    Diagnostics = new Container<Diagnostic>(value.GetDiagnostics()),
                    Uri = value.DocumentUri,
                    Version = value.Version
                });
            }

            return Unit.Task;
        }

        public void SetCapability(SynchronizationCapability capability) { _capability = capability; }
        public TextDocumentChangeRegistrationOptions GetRegistrationOptions() { return _options; }

        TextDocumentRegistrationOptions IRegistration<TextDocumentRegistrationOptions>.GetRegistrationOptions() { return _options; }

        TextDocumentSaveRegistrationOptions IRegistration<TextDocumentSaveRegistrationOptions>.GetRegistrationOptions() { return _saveOptions; }
    }
}
