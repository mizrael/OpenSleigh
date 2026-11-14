using System.Collections.Concurrent;

namespace OpenSleigh;

// SPIKE, not for merging: switches and counters for the ledger persistence experiment.

public enum LedgerConcurrencyMode
{
    /// <summary>nothing is written before the handler: the append is refused when the saga changed in the meantime.</summary>
    NoLock,

    /// <summary>a lease is written on the saga before the handler, as the lock is today.</summary>
    Lease
}

public static class LedgerSpike
{
    private const string ModeVariable = "OPENSLEIGH_SPIKE_MODE";

    private static long _appendConflicts;
    private static long _inProcessRetries;
    private static long _conflictsLeftToTransport;

    public static LedgerConcurrencyMode Mode { get; set; } = ModeFromEnvironment();

    /// <summary>how many times a message is handled again in the process after its append was refused.</summary>
    public static int ConflictRetries { get; set; } = 3;

    /// <summary>appends refused because the saga had changed since it was loaded.</summary>
    public static long AppendConflicts => Interlocked.Read(ref _appendConflicts);

    /// <summary>messages handled again in the process after a refused append.</summary>
    public static long InProcessRetries => Interlocked.Read(ref _inProcessRetries);

    /// <summary>messages given back to the transport after the retries in the process were used up.</summary>
    public static long ConflictsLeftToTransport => Interlocked.Read(ref _conflictsLeftToTransport);

    public static void CountAppendConflict() => Interlocked.Increment(ref _appendConflicts);
    public static void CountInProcessRetry() => Interlocked.Increment(ref _inProcessRetries);
    public static void CountConflictLeftToTransport() => Interlocked.Increment(ref _conflictsLeftToTransport);

    public static void ResetCounters()
    {
        Interlocked.Exchange(ref _appendConflicts, 0);
        Interlocked.Exchange(ref _inProcessRetries, 0);
        Interlocked.Exchange(ref _conflictsLeftToTransport, 0);
    }

    private static LedgerConcurrencyMode ModeFromEnvironment()
        => Enum.TryParse<LedgerConcurrencyMode>(Environment.GetEnvironmentVariable(ModeVariable), ignoreCase: true, out var mode)
            ? mode
            : LedgerConcurrencyMode.NoLock;
}

/// <summary>
/// lets one message at a time through for a saga instance, within this process.
/// </summary>
public sealed class SagaInstanceGate
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public async ValueTask<IDisposable> EnterAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        while (true)
        {
            entry = _entries.GetOrAdd(key, static _ => new Entry());
            lock (entry)
            {
                if (entry.Removed)
                    continue;
                entry.Users++;
                break;
            }
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Leave(key, entry, release: false);
            throw;
        }

        return new Exit(this, key, entry);
    }

    private void Leave(string key, Entry entry, bool release)
    {
        if (release)
            entry.Semaphore.Release();

        lock (entry)
        {
            if (--entry.Users == 0)
            {
                entry.Removed = true;
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
        public bool Removed;
    }

    private sealed class Exit(SagaInstanceGate gate, string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Leave(key, entry, release: true);
        }
    }
}
