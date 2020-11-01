using System;
using System.Reactive.Disposables;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace server.tests
{
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
