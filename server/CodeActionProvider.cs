using System;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using MediatR;
using System.Threading;
using parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;

namespace server
{
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
}
