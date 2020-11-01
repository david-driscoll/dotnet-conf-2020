using System;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Threading;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using System.Linq;
using System.Collections.Immutable;
using parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using System.Security.Cryptography;
using System.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Document.Proposals;
using OmniSharp.Extensions.LanguageServer.Protocol.Models.Proposals;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace.Proposals;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using System.Buffers;

namespace server
{
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
}
