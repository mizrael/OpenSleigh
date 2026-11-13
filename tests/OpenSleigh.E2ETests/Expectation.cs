using System.Collections.Concurrent;

namespace OpenSleigh.E2ETests;

/// <summary>
/// counts what the sagas of a scenario report, from whatever thread they run on,
/// and tells when the expected number was reached.
/// </summary>
internal sealed class Expectation
{
    private readonly int _expectedCount;
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<string> _problems = new();
    private int _count;

    public Expectation(int expectedCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedCount, 1);
        _expectedCount = expectedCount;
    }

    public int Count => Volatile.Read(ref _count);

    public Task Reached => _reached.Task;

    /// <summary>what the handlers found wrong. An assertion thrown inside a handler would only make the message fail.</summary>
    public IReadOnlyCollection<string> Problems => _problems;

    public void Signal()
    {
        if (Interlocked.Increment(ref _count) >= _expectedCount)
            _reached.TrySetResult();
    }

    public void Require(bool condition, string problem)
    {
        if (!condition)
            _problems.Enqueue(problem);
    }
}
