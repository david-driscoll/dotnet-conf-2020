using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
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
        public async Task Should_Return_Diagnostics()
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

            await TestHelper.DelayUntil(() => diagnostics.Count > 0, CancellationToken);

            Assert.Single(diagnostics);
            Assert.Equal("NINWISH", diagnostics[0].Code);
            Assert.Equal("Key is not complete", diagnostics[0].Message);
        }

        [Fact]
        public async Task Should_Return_Completion_Items()
        {
            var (client, configuration) = await InitializeClientWithConfiguration(options => { });

            client.TextDocument.DidOpenTextDocument(new DidOpenTextDocumentParams()
            {
                TextDocument = new TextDocumentItem()
                {
                    LanguageId = "ini",
                    Uri = DocumentUri.FromFileSystemPath("/some/path/file.ini"),
                    Text = @"[Central]
AccountabilityAssociate=John

[Principal]
AssuranceAgent=Hello Gerry
                    ",
                    Version = 1
                }
            });

            var results = await client.TextDocument.RequestCompletion(new CompletionParams()
            {
                TextDocument = new TextDocumentIdentifier()
                {
                    Uri = DocumentUri.FromFileSystemPath("/some/path/file.ini"),
                },
                Position = (1, 24)
            }, CancellationToken);

            Assert.Equal(50, results.Count());
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


    public static class TestHelper
    {
        public static async Task DelayUntil<T>(Func<T> valueFunc, Func<T, bool> func, CancellationToken cancellationToken, TimeSpan? delay = null)
        {
            while (true)
            {
                if (func(valueFunc())) return;
                await Task.Delay(delay ?? TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }

        public static async Task DelayUntil(Func<bool> func, CancellationToken cancellationToken, TimeSpan? delay = null)
        {
            while (true)
            {
                if (func()) return;
                await Task.Delay(delay ?? TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }

        public static Task DelayUntil<T>(this T value, Func<T, bool> func, CancellationToken cancellationToken, TimeSpan? delay = null)
        {
            return DelayUntil(() => value, func, cancellationToken, delay);
        }

        public static Task DelayUntilCount<T>(this T value, int count, CancellationToken cancellationToken, TimeSpan? delay = null) where T : IEnumerable
        {
            return DelayUntil(() => value.OfType<object>().Count() >= count, cancellationToken, delay);
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
