using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace server
{
    class Data : HandlerIdentity
    {
        public string Section { get; set; }
        public string Key { get; set; }
        public int Seed { get; set; }
    }
}
