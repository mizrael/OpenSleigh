using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

/// <summary>
/// SPIKE: what a scenario measured besides the one value it returns. Written next to the run report.
/// </summary>
internal static class SpikeMetrics
{
    private static readonly ConcurrentQueue<Dictionary<string, object>> Recorded = new();

    public static void Record(string scenario, IEnumerable<(string Name, double Value)> values)
    {
        var entry = new Dictionary<string, object> { ["scenario"] = scenario };
        foreach (var (name, value) in values)
            entry[name] = Math.Round(value, 4);

        Recorded.Enqueue(entry);
        Console.WriteLine("    " + string.Join(", ", entry.Select(pair =>
            $"{pair.Key}={Convert.ToString(pair.Value, CultureInfo.InvariantCulture)}")));
    }

    public static void WriteTo(string path, string variant, int round)
    {
        var document = new { variant, round, metrics = Recorded.ToArray() };
        File.WriteAllText(path, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>
/// SPIKE: runs a scenario with the saga store in one of its two modes. Built against a source tree that has no
/// such modes (the base branch), both run the same thing.
/// </summary>
internal static class SpikeMode
{
    public static async Task<double> NoLockAsync(Func<Task<double>> scenario)
    {
        Set(lease: false);
        return await scenario();
    }

    public static async Task<double> LeaseAsync(Func<Task<double>> scenario)
    {
        Set(lease: true);
        try
        {
            return await scenario();
        }
        finally
        {
            Set(lease: false);
        }
    }

    public static IEnumerable<(string Name, double Value)> Counters()
    {
#if LEDGER_SPIKE
        yield return ("leaseMode", OpenSleigh.LedgerSpike.Mode == OpenSleigh.LedgerConcurrencyMode.Lease ? 1 : 0);
        yield return ("appendConflicts", OpenSleigh.LedgerSpike.AppendConflicts);
        yield return ("inProcessRetries", OpenSleigh.LedgerSpike.InProcessRetries);
        yield return ("conflictsLeftToTransport", OpenSleigh.LedgerSpike.ConflictsLeftToTransport);
#else
        yield break;
#endif
    }

    public static void ResetCounters()
    {
#if LEDGER_SPIKE
        OpenSleigh.LedgerSpike.ResetCounters();
#endif
    }

    private static void Set(bool lease)
    {
#if LEDGER_SPIKE
        OpenSleigh.LedgerSpike.Mode = lease ? OpenSleigh.LedgerConcurrencyMode.Lease : OpenSleigh.LedgerConcurrencyMode.NoLock;
#endif
    }
}
