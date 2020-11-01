using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace server.tests
{
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
}
