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

namespace server
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            var (input, output) = await CreateNamedPipe();
            var server = await LanguageServer.From(options => ConfigureServer(options, input, output));
            await server.WaitForExit;
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
                    services.AddSingleton<CompletionProvider>();
                    services.AddSingleton<HoverProvider>();
                    services.AddSingleton<TextDocumentStore>();
                    services.AddSingleton<TokenProvider>();
                    services.AddSingleton<OutlineProvider>();
                    services.AddSingleton<CodeActionProvider>();
                    services.AddSingleton<CodeActionProvider.CommandHandler>();
                    services.AddSingleton<CodeLensProvider>();
                    services.AddSingleton<FoldingRangeProvider>();
                    services.AddSingleton<SelectionRangeProvider>();
                    services
                        .ConfigureSection<IniConfiguration>("ini")
                        .ConfigureSection<NinConfiguration>("nin");
                })
                .WithConfigurationSection("ini")
                .WithConfigurationSection("nin");
        }

        private static async Task<(PipeReader input, PipeWriter output)> CreateNamedPipe()
        {

            var pipe = new NamedPipeServerStream(
                            pipeName: @"ninrocks",
                            direction: PipeDirection.InOut,
                            maxNumberOfServerInstances: 1,
                            transmissionMode: PipeTransmissionMode.Byte,
                            options: System.IO.Pipes.PipeOptions.CurrentUserOnly | System.IO.Pipes.PipeOptions.Asynchronous);
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

            services.AddOptions();
            services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(
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
