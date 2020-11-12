using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace client.vs
{
    public class NinContentDefinition
    {
        [Export]
        [Name("nin")]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        internal static ContentTypeDefinition NinContentTypeDefinition;

        [Export]
        [FileExtension(".nin")]
        [ContentType("nin")]
        internal static FileExtensionToContentTypeDefinition NinFileExtensionDefinition;
    }
}
