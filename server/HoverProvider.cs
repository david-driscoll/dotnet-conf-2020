using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Threading;
using parser;

namespace server
{
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
}
