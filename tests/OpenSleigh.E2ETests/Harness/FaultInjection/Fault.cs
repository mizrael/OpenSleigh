namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>what a service of a host throws while a <see cref="Fault"/> is on.</summary>
public sealed class InjectedFailureException(string message) : Exception(message);

/// <summary>
/// a failure that the scenario turns on and off: while it is on, the services it was given to throw instead of
/// doing their work. The scenario decides when it starts and ends, and waits for <see cref="Hits"/>.
/// </summary>
public sealed class Fault(string description)
{
    private const int Off = 0;
    private const int UntilStopped = -1;

    private int _failuresLeft = Off;
    private int _hits;

    /// <summary>how many times a service failed because of this fault.</summary>
    public int Hits => Volatile.Read(ref _hits);

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
    public void ThrowIfOn()
    {
        if (!TryTakeFailure())
            return;

        Interlocked.Increment(ref _hits);
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
}
