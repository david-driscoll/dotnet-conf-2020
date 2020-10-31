using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc.Testing;
using OmniSharp.Extensions.LanguageProtocol.Testing;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using Xunit;
using Xunit.Abstractions;

namespace server.tests
{
    public class UnitTest1 : LanguageServerTestBase
    {
        [Fact]
        public async Task Test1()
        {
            var diagnostics = new List<Diagnostic>();
            var (client, configuration) = await InitializeClientWithConfiguration(options =>
            {
                options.OnPublishDiagnostics((request) =>
                {
                    diagnostics.AddRange(request.Diagnostics);
                });
            });

            client.TextDocument.DidOpenTextDocument(new DidOpenTextDocumentParams()
            {
                TextDocument = new TextDocumentItem()
                {
                    LanguageId = "ini",
                    Uri = DocumentUri.FromFileSystemPath("/some/path/file.ini"),
                    Text = @"[Central
AccountabilityAssociate=John

[Principal]
AssuranceAgent=Hello Gerry
                    ",
                    Version = 1
                }
            });

            await SettleNext();

            Assert.Single(diagnostics);
        }


        public UnitTest1(
            ITestOutputHelper testOutputHelper
        ) : base(
            new JsonRpcTestOptions()
            .WithClientLoggerFactory(new TestLoggerFactory(testOutputHelper))
            .WithServerLoggerFactory(new TestLoggerFactory(testOutputHelper))
        )
        {

        }

        protected override (Stream clientOutput, Stream serverInput) SetupServer()
        {
            var clientPipe = new Pipe(TestOptions.DefaultPipeOptions);
            var serverPipe = new Pipe(TestOptions.DefaultPipeOptions);

            var server = LanguageServer.PreInit(options =>
            {
                global::server.Program.ConfigureServer(options, serverPipe.Reader, clientPipe.Writer);
                // options.WithInput(serverPipe.Reader).WithOutput(clientPipe.Writer);
            });

            server.Initialize(CancellationToken);

            return (clientPipe.Reader.AsStream(), serverPipe.Writer.AsStream());
        }
    }

    class TestLoggerFactory : ILoggerFactory
    {
        private readonly ITestOutputHelper outputHelper;

        public TestLoggerFactory(ITestOutputHelper outputHelper)
        {
            this.outputHelper = outputHelper;
        }
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(outputHelper);
        }

        public void Dispose()
        {
        }
    }

    class TestLogger : ILogger
    {
        private readonly ITestOutputHelper outputHelper;

        public TestLogger(ITestOutputHelper outputHelper)
        {
            this.outputHelper = outputHelper;
        }
        public IDisposable BeginScope<TState>(TState state)
        {
            return Disposable.Empty;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            outputHelper.WriteLine(logLevel.ToString() + ": " + formatter(state, exception));
        }
    }
}
