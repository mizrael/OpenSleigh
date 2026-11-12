using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

/// <summary>
/// Collects warnings and errors logged by the libraries while a scenario runs,
/// so a failed or suspicious run can be explained without flooding the job log.
/// </summary>
internal sealed class ProblemLog : ILoggerProvider
{
    private const int MaxSamples = 3;

    private readonly ConcurrentQueue<string> _samples = new();
    private int _count;

    public int Count => _count;

    public void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
        _samples.Clear();
    }

    public string Describe()
        => _count == 0 ? string.Empty : $"{_count} warning(s)/error(s) logged, e.g.: {string.Join(" | ", _samples)}";

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private void Add(string category, LogLevel level, string message, Exception? exception)
    {
        Interlocked.Increment(ref _count);
        if (_samples.Count >= MaxSamples)
            return;

        var text = $"[{level}] {category}: {message}";
        if (exception is not null)
            text += $" ({exception.GetType().Name}: {exception.Message})";
        _samples.Enqueue(text.Length > 400 ? text[..400] : text);
    }

    private sealed class Logger(ProblemLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                owner.Add(category, logLevel, formatter(state, exception), exception);
        }
    }
}
