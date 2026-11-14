using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// sends what the hosts log to the output of the test that started them.
/// </summary>
internal sealed class TestOutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TestOutputLogger(output, categoryName);

    public void Dispose() { }

    private sealed class TestOutputLogger(ITestOutputHelper output, string categoryName) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                output.WriteLine($"{categoryName}/{logLevel}: {formatter(state, exception)}");

                if (exception is not null)
                    output.WriteLine(exception.ToString());
            }
            catch (InvalidOperationException)
            {
                // a host that is still shutting down logs after its test is over, when xunit takes no more output
            }
        }
    }
}
