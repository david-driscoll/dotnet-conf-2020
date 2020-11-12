using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace client.vs
{
    public class IniContentDefinition
    {
        [Export]
        [Name("ini")]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        internal static ContentTypeDefinition IniContentTypeDefinition;

        [Export]
        [FileExtension(".ini")]
        [ContentType("ini")]
        internal static FileExtensionToContentTypeDefinition IniFileExtensionDefinition;
    }
}
