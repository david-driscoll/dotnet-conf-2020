using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO.Pipes;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Task = System.Threading.Tasks.Task;

namespace client.vs
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(clientvsPackage.PackageGuidString)]
    public sealed class clientvsPackage : AsyncPackage
    {
        /// <summary>
        /// client.vsPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "a82f879d-0b40-4a87-b733-c92ed7c0cfb8";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        }

        #endregion
    }


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
