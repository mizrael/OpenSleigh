using OpenSleigh.Benchmarks.Throughput.Scenarios;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

internal static class Runner
{
    private static readonly TimeSpan ScenarioTimeout = TimeSpan.FromMinutes(4);

    public static async Task<RunReport> RunAsync(IReadOnlyList<Scenario> scenarios, string suite, string variant, int round, int runs)
    {
        var settings = Settings.FromEnvironment();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var results = new List<ScenarioResult>(scenarios.Count);

        foreach (var scenario in scenarios)
        {
            var samples = new List<double>(runs);
            var errors = new List<string>();

            for (var run = 0; run < runs; run++)
            {
                var problems = new ProblemLog();
                var context = new ScenarioContext(settings, problems);
                var runStopwatch = Stopwatch.StartNew();

                // a scenario measured over a tenth of a second is slowed down a lot by one collection:
                // whether one falls inside it must not depend on how much the scenarios before it allocated
                CollectGarbage();

                try
                {
                    var value = await scenario.RunAsync(context).WaitAsync(ScenarioTimeout);
                    if (double.IsFinite(value) && value > 0)
                    {
                        samples.Add(value);
                        Console.WriteLine($"[{variant}] {scenario.Name}: {Statistics.Format(value)} {scenario.Unit} ({runStopwatch.Elapsed.TotalSeconds:N1} s)");
                    }
                    else
                    {
                        errors.Add($"no work completed. {problems.Describe()}".Trim());
                        Console.WriteLine($"[{variant}] {scenario.Name}: no work completed. {problems.Describe()}");
                    }
                }
                catch (Exception ex)
                {
                    var error = $"{ex.GetType().Name}: {ex.Message} {problems.Describe()}".Trim();
                    errors.Add(error);
                    Console.WriteLine($"[{variant}] {scenario.Name}: FAILED {error}");
                }
            }

            results.Add(new ScenarioResult(scenario.Name, scenario.Unit, scenario.HigherIsBetter, scenario.Workload, samples, errors));
        }

        return new RunReport(variant, round, suite, startedAt, stopwatch.Elapsed.TotalSeconds, DescribeMachine(), results);
    }

    public static string ToMarkdown(RunReport report)
    {
        var lines = new List<string>
        {
            $"Variant `{report.Variant}`, suite `{report.Suite}`, {report.DurationSeconds:N0} s, {report.Machine}",
            string.Empty,
            "| Scenario | Unit | Median | Min | Max | Runs | Spread | Workload |",
            "|---|---|---:|---:|---:|---:|---:|---|"
        };

        foreach (var result in report.Results)
        {
            var failed = result.Errors.Count > 0 ? $" ({result.Errors.Count} failed: {result.Errors[0]})" : string.Empty;
            lines.Add(result.Samples.Count == 0
                ? $"| {result.Name} | {result.Unit} | n/a | n/a | n/a | 0 | n/a | {result.Workload}{failed} |"
                : $"| {result.Name} | {result.Unit} | {Statistics.Format(Statistics.Median(result.Samples))} | {Statistics.Format(result.Samples.Min())} | {Statistics.Format(result.Samples.Max())} | {result.Samples.Count} | {FormatSpread(result.Samples)} | {result.Workload}{failed} |");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static string FormatSpread(IReadOnlyCollection<double> samples)
    {
        var spread = Statistics.RelativeSpread(samples);
        return double.IsNaN(spread) ? "n/a" : spread.ToString("0%", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One table per profiled run. Stage times are wall-clock time inside the call and overlap between
    /// the publishing callers, the relay and the consumer, so they do not add up to the elapsed time.
    /// </summary>
    public static IEnumerable<string> ProfileToMarkdown(string variant, string name, EndToEndProfile profile)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        yield return $"Variant `{variant}`, {name}: {profile.Sagas} sagas in {profile.ElapsedMilliseconds.ToString("N0", culture)} ms";
        yield return string.Empty;
        yield return "| Call | Calls | Total ms | Mean ms | ms per saga |";
        yield return "|---|---:|---:|---:|---:|";

        foreach (var (stage, calls, total) in profile.Stages)
            yield return $"| {stage} | {calls} | {total.ToString("N0", culture)} | {(total / Math.Max(calls, 1)).ToString("N3", culture)} | {(total / Math.Max(profile.Sagas, 1)).ToString("N3", culture)} |";
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string DescribeMachine()
        => $"{RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical cores, {RuntimeInformation.FrameworkDescription}";
}
