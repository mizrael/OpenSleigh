namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

internal sealed record ScenarioGate(bool Gate = true, double? Tolerance = null);

/// <summary>
/// What "worse" means: a scenario regresses when the head is slower than the base by more than its tolerance.
/// </summary>
internal sealed record GateConfiguration(double DefaultTolerance, int MinimumRounds, Dictionary<string, ScenarioGate>? Scenarios)
{
    public static readonly GateConfiguration Default = new(0.30, 3, null);

    public ScenarioGate For(string scenarioName)
        => Scenarios is not null && Scenarios.TryGetValue(scenarioName, out var gate) ? gate : new ScenarioGate();

    public double ToleranceFor(string scenarioName) => For(scenarioName).Tolerance ?? DefaultTolerance;
}

internal enum Verdict
{
    WithinTolerance,
    Improved,
    Regression,
    WorseNotGated,
    Inconclusive,
    HeadFailed,
    NoData
}

internal sealed record ScenarioComparison(
    string Name,
    string Unit,
    IReadOnlyList<double> Base,
    IReadOnlyList<double> Head,
    int PairedRounds,
    double MedianChange,
    double Tolerance,
    Verdict Verdict);

internal static class Comparison
{
    public const string BaseVariant = "base";
    public const string HeadVariant = "head";

    public static IReadOnlyList<ScenarioComparison> Compare(IReadOnlyList<RunReport> reports, GateConfiguration configuration)
    {
        var baseRounds = reports.Where(r => r.Variant == BaseVariant).ToArray();
        var headRounds = reports.Where(r => r.Variant == HeadVariant).ToArray();

        var names = headRounds.Concat(baseRounds)
            .SelectMany(r => r.Results)
            .Select(r => r.Name)
            .Distinct()
            .ToArray();

        return names.Select(name => CompareScenario(name, baseRounds, headRounds, configuration)).ToArray();
    }

    public static bool Fails(ScenarioComparison comparison)
        => comparison.Verdict is Verdict.Regression or Verdict.HeadFailed;

    private static ScenarioComparison CompareScenario(string name, RunReport[] baseRounds, RunReport[] headRounds, GateConfiguration configuration)
    {
        var baseByRound = ValuesByRound(name, baseRounds);
        var headByRound = ValuesByRound(name, headRounds);
        var definition = baseRounds.Concat(headRounds).SelectMany(r => r.Results).First(r => r.Name == name);
        var gate = configuration.For(name);
        var tolerance = configuration.ToleranceFor(name);

        // rounds are paired because base and head ran back to back: whatever slowed the runner down hit both
        var changes = baseByRound.Keys.Intersect(headByRound.Keys)
            .Select(round => definition.HigherIsBetter
                ? headByRound[round] / baseByRound[round] - 1
                : baseByRound[round] / headByRound[round] - 1)
            .ToArray();
        var medianChange = Statistics.Median(changes);

        Verdict verdict;
        if (headByRound.Count == 0)
            verdict = baseByRound.Count == 0 ? Verdict.NoData : Verdict.HeadFailed;
        else if (changes.Length < configuration.MinimumRounds)
            verdict = Verdict.Inconclusive;
        else if (medianChange < -tolerance)
            verdict = gate.Gate ? Verdict.Regression : Verdict.WorseNotGated;
        else if (medianChange > tolerance)
            verdict = Verdict.Improved;
        else
            verdict = Verdict.WithinTolerance;

        return new ScenarioComparison(
            name,
            definition.Unit,
            baseByRound.Values.ToArray(),
            headByRound.Values.ToArray(),
            changes.Length,
            medianChange,
            tolerance,
            verdict);
    }

    private static Dictionary<int, double> ValuesByRound(string name, RunReport[] rounds)
        => rounds
            .Select(r => (r.Round, Samples: r.Results.FirstOrDefault(s => s.Name == name)?.Samples))
            .Where(r => r.Samples is { Count: > 0 })
            .GroupBy(r => r.Round)
            .ToDictionary(g => g.Key, g => Statistics.Median(g.SelectMany(r => r.Samples!).ToArray()));

    public static string ToMarkdown(IReadOnlyList<ScenarioComparison> comparisons)
    {
        var lines = new List<string>
        {
            "| Scenario | Unit | Base median (min-max) | Head median (min-max) | Rounds | Change | Tolerance | Verdict |",
            "|---|---|---:|---:|---:|---:|---:|---|"
        };

        foreach (var c in comparisons)
            lines.Add($"| {c.Name} | {c.Unit} | {Describe(c.Base)} | {Describe(c.Head)} | {c.PairedRounds} | {Statistics.FormatPercent(c.MedianChange)} | {c.Tolerance:0%} | {Describe(c.Verdict)} |");

        lines.Add(string.Empty);
        lines.Add("Change is the median, over paired rounds, of how much better (+) or worse (-) the head did than the base in the same round.");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Describe(IReadOnlyList<double> values)
        => values.Count == 0
            ? "n/a"
            : $"{Statistics.Format(Statistics.Median(values))} ({Statistics.Format(values.Min())}-{Statistics.Format(values.Max())})";

    private static string Describe(Verdict verdict) => verdict switch
    {
        Verdict.WithinTolerance => "ok",
        Verdict.Improved => "improved",
        Verdict.Regression => "**REGRESSION**",
        Verdict.WorseNotGated => "worse (not gated)",
        Verdict.Inconclusive => "inconclusive (too few rounds)",
        Verdict.HeadFailed => "**FAILED on head**",
        _ => "no data"
    };
}
