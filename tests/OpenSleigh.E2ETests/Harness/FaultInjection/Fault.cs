using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>what a service of a host throws while a <see cref="Fault"/> is on.</summary>
public sealed class InjectedFailureException(string message) : Exception(message);

/// <param name="Source">what failed: the host, when the fault was given one.</param>
/// <param name="Timestamp">a <see cref="Stopwatch"/> timestamp.</param>
public readonly record struct FaultHit(object Source, long Timestamp);

/// <summary>
/// a failure that the scenario turns on and off: while it is on, the services it was given to throw instead of
/// doing their work. The scenario decides when it starts and ends, and waits for it to have been hit.
/// </summary>
public sealed class Fault(string description)
{
    private const int Off = 0;
    private const int UntilStopped = -1;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    private readonly ConcurrentQueue<FaultHit> _hits = new();
    private int _failuresLeft = Off;

    /// <summary>every time a service failed because of this fault, in order.</summary>
    public IReadOnlyCollection<FaultHit> Hits => _hits;

    /// <summary>fails every call from now on, until <see cref="Stop"/>.</summary>
    public void Start() => Volatile.Write(ref _failuresLeft, UntilStopped);

    /// <summary>fails the next calls, that many of them, and then no more.</summary>
    public void StartFor(int failures)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failures, 1);
        Volatile.Write(ref _failuresLeft, failures);
    }

    public void Stop() => Volatile.Write(ref _failuresLeft, Off);

    /// <exception cref="InjectedFailureException">the fault is on.</exception>
    public void ThrowIfOn(object source)
    {
        if (!TryTakeFailure())
            return;

        _hits.Enqueue(new FaultHit(source, Stopwatch.GetTimestamp()));
        throw new InjectedFailureException(description);
    }

    private bool TryTakeFailure()
    {
        while (true)
        {
            var left = Volatile.Read(ref _failuresLeft);
            if (left == Off)
                return false;
            if (left == UntilStopped)
                return true;
            if (Interlocked.CompareExchange(ref _failuresLeft, left - 1, left) == left)
                return true;
        }
    }

    /// <summary>returns once the fault made that many calls fail.</summary>
    public async Task WaitForHitsAsync(int count, CancellationToken cancellationToken)
    {
        while (_hits.Count < count)
            await Task.Delay(PollingInterval, cancellationToken);
    }

    /// <summary>
    /// the shortest time between two failures in a row of the same source: how fast a host tried again.
    /// Null when no source failed twice.
    /// </summary>
    public TimeSpan? ShortestTimeBetweenHitsOfOneSource()
    {
        var gaps = from hits in _hits.GroupBy(hit => hit.Source)
                   let timestamps = hits.Select(hit => hit.Timestamp).Order().ToArray()
                   from index in Enumerable.Range(1, Math.Max(0, timestamps.Length - 1))
                   select Stopwatch.GetElapsedTime(timestamps[index - 1], timestamps[index]);

        return gaps.Cast<TimeSpan?>().Min();
    }
}
