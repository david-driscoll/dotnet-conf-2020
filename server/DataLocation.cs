using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace server
{
    class DataLocation : HandlerIdentity
    {
        public Location Location { get; set; }
    }
}
