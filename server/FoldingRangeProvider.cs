using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Threading;
using System.Linq;
using System.Buffers;

namespace server
{
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
}
