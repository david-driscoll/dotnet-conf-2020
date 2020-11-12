using System;
using OmniSharp.Extensions.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Server;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;
using System.IO.Pipes;
using System.IO.Pipelines;
using Nerdbank.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Diagnostics;

namespace server
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            while (true)
            {
                await Console.Error.WriteLineAsync("Waiting for client to connect on pipe...");
                var (input, output) = await CreateNamedPipe();
                var server = await LanguageServer.From(options => ConfigureServer(options, input, output));
                await Console.Error.WriteLineAsync("Client connected!");

                await Task.WhenAny(
                    Task.Run(async () =>
                    {
                        while (true)
                        {
                            await Task.Delay(1000);
                            if (server.ClientSettings.ProcessId.HasValue && Process.GetProcessById((int)server.ClientSettings.ProcessId.Value).HasExited)
                            {
                                await Console.Error.WriteLineAsync("Client disappeared restarting");
                                server.ForcefulShutdown();
                                return;
                            }
                        }
                    }),
                    server.WaitForExit
                );
                await Console.Error.WriteLineAsync("Server was shutdown restarting...");
            }
        }

        public static void ConfigureServer(LanguageServerOptions options, PipeReader input, PipeWriter output)
        {
            options
                .WithInput(input)
                .WithOutput(output)
                .ConfigureLogging(
                    x => x
                        .ClearProviders()
                        .AddLanguageProtocolLogging()
                        .SetMinimumLevel(LogLevel.Debug)
                )
                .WithServices(services =>
                {
                    services
                        .AddSingleton<TextDocumentStore>()
                        // .AddSingleton<CompletionProvider>()
                        // .AddSingleton<HoverProvider>()
                        // .AddSingleton<TokenProvider>()
                        // .AddSingleton<OutlineProvider>()
                        // .AddSingleton<CodeActionProvider>()
                        // .AddSingleton<CodeActionProvider.CommandHandler>()
                        // .AddSingleton<CodeLensProvider>()
                        // .AddSingleton<FoldingRangeProvider>()
                        // .AddSingleton<SelectionRangeProvider>()
                        .ConfigureSection<IniConfiguration>("ini")
                        .ConfigureSection<NinConfiguration>("nin")
                        ;
                })
                .WithConfigurationSection("ini")
                .WithConfigurationSection("nin")
                .OnInitialized((instance, client, server, ct) =>
                {
                    // Bug in visual studio support where CodeActionKind.Empty is not supported, and throws (instead of gracefully ignoring it)
                    if (server?.Capabilities?.CodeActionProvider?.Value?.CodeActionKinds != null)
                    {
                        server.Capabilities.CodeActionProvider.Value.CodeActionKinds = server.Capabilities.CodeActionProvider.Value.CodeActionKinds.ToImmutableArray().Remove(CodeActionKind.Empty).ToArray();
                    }
                    return Task.CompletedTask;
                });
        }

        private static async Task<(PipeReader input, PipeWriter output)> CreateNamedPipe()
        {
            var pipe = new NamedPipeServerStream(
                            pipeName: @"ninrocks",
                            direction: PipeDirection.InOut,
                            maxNumberOfServerInstances: 1,
                            transmissionMode: PipeTransmissionMode.Byte,
                            options: System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync();
            var pipeline = pipe.UsePipe();
            // await pipe.WaitForConnectionAsync().ConfigureAwait(false);
            return (pipeline.Input, pipeline.Output);
        }

        public static IServiceCollection ConfigureSection<TOptions>(this IServiceCollection services, string? sectionName)
            where TOptions : class
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOptions()
                .AddSingleton<IOptionsChangeTokenSource<TOptions>>(
                _ => new ConfigurationChangeTokenSource<TOptions>(
                    Options.DefaultName,
                    sectionName == null ? _.GetRequiredService<IConfiguration>() : _.GetRequiredService<IConfiguration>().GetSection(sectionName)
                )
            );
            return services.AddSingleton<IConfigureOptions<TOptions>>(
                _ => new NamedConfigureFromConfigurationOptions<TOptions>(
                    Options.DefaultName,
                    sectionName == null ? _.GetRequiredService<IConfiguration>() : _.GetRequiredService<IConfiguration>().GetSection(sectionName)
                )
            );
        }
    }
}
