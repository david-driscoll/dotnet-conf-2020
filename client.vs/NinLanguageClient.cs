using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Task = System.Threading.Tasks.Task;

namespace client.vs
{
    [ContentType("ini"), ContentType("nin")]
    [Export(typeof(ILanguageClient))]
    public class NinLanguageClient : ILanguageClient
    {
        public string Name => "Nin Language Extension";

        public IEnumerable<string> ConfigurationSections => new[] { "ini", "nin" };

        public object InitializationOptions => null;

        public IEnumerable<string> FilesToWatch => null;

        public event AsyncEventHandler<EventArgs> StartAsync;
        public event AsyncEventHandler<EventArgs> StopAsync;

        public async Task<Connection> ActivateAsync(CancellationToken token)
        {
            await Task.Yield();

            while (true)
            {
                try
                {
                    var namedPipe = new NamedPipeClientStream(
                        pipeName: $@"ninrocks",
                        direction: PipeDirection.InOut, 
                        serverName: ".",
                        options: PipeOptions.Asynchronous);

                    await namedPipe.ConnectAsync(token);

                    //await namedPipe.WaitForConnectionAsync(token);
                    return new Connection(namedPipe, namedPipe);
                }
                catch (Exception e)
                {
                    await Task.Delay(5000);
                }
            }
        }

        public async Task OnLoadedAsync()
        {
            await StartAsync.InvokeAsync(this, EventArgs.Empty);
        }

        public Task OnServerInitializeFailedAsync(Exception e)
        {
            return Task.CompletedTask;
        }

        public Task OnServerInitializedAsync()
        {
            return Task.CompletedTask;
        }
    }
}
