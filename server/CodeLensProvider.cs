using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using System.Threading;
using System.Linq;
using System.Buffers;

namespace server
{
    class CodeLensProvider : CodeLensHandlerBase<CodeLensProvider.CodeLensData>
    {

        public class CodeLensData : HandlerIdentity
        {
            public DocumentUri Uri { get; set; }
            public string Section { get; set; }
        }
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
}
