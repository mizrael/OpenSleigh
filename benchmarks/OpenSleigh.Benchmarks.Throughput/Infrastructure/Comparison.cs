namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

/// <param name="MinimumDifference">the absolute floor of this scenario, in its unit. None when not set.</param>
internal sealed record ScenarioGate(bool Gate = true, double? Tolerance = null, double? MinimumDifference = null);

/// <summary>
/// What "worse" means: a scenario regresses when the head is slower than the base by more than its tolerance
/// and, for a scenario that has one, by at least its minimum difference. The tolerance is relative, and a relative
/// change means little between two values that are both next to zero: the minimum difference is the absolute floor
/// under it. It is set per scenario, never for a whole unit: it has to be small against what that scenario measures.
/// </summary>
internal sealed record GateConfiguration(
    double DefaultTolerance,
    int MinimumRounds,
    Dictionary<string, ScenarioGate>? Scenarios)
{
    public const double NoMinimumDifference = 0;

    public static readonly GateConfiguration Default = new(0.30, 3, null);

    public ScenarioGate For(string scenarioName)
        => Scenarios is not null && Scenarios.TryGetValue(scenarioName, out var gate) ? gate : new ScenarioGate();

    public double ToleranceFor(string scenarioName) => For(scenarioName).Tolerance ?? DefaultTolerance;

    public double MinimumDifferenceFor(string scenarioName) => For(scenarioName).MinimumDifference ?? NoMinimumDifference;
}

internal enum Verdict
{
    WithinTolerance,
    BelowMinimumDifference,
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
    double MedianDifference,
    double MinimumDifference,
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
        var pairedRounds = baseByRound.Keys.Intersect(headByRound.Keys).ToArray();
        var changes = pairedRounds
            .Select(round => definition.HigherIsBetter
                ? headByRound[round] / baseByRound[round] - 1
                : baseByRound[round] / headByRound[round] - 1)
            .ToArray();
        var medianChange = Statistics.Median(changes);

        // the same comparison in the unit of the scenario, with the same sign: positive when the head did better
        var medianDifference = Statistics.Median(pairedRounds
            .Select(round => definition.HigherIsBetter
                ? headByRound[round] - baseByRound[round]
                : baseByRound[round] - headByRound[round])
            .ToArray());
        var minimumDifference = configuration.MinimumDifferenceFor(name);

        Verdict verdict;
        if (headByRound.Count == 0)
            verdict = baseByRound.Count == 0 ? Verdict.NoData : Verdict.HeadFailed;
        else if (changes.Length < configuration.MinimumRounds)
            verdict = Verdict.Inconclusive;
        else if (Math.Abs(medianChange) > tolerance && Math.Abs(medianDifference) < minimumDifference)
            // beyond the tolerance, either way, by an amount too small to be told from noise
            verdict = Verdict.BelowMinimumDifference;
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
            medianDifference,
            minimumDifference,
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
            "| Scenario | Unit | Base median (min-max) | Head median (min-max) | Rounds | Change | Tolerance | Difference | Floor | Verdict |",
            "|---|---|---:|---:|---:|---:|---:|---:|---:|---|"
        };

        foreach (var c in comparisons)
            lines.Add($"| {c.Name} | {c.Unit} | {Describe(c.Base)} | {Describe(c.Head)} | {c.PairedRounds} | {Statistics.FormatPercent(c.MedianChange)} | {c.Tolerance:0%} | {DescribeDifference(c.MedianDifference)} | {DescribeFloor(c.MinimumDifference)} | {Describe(c)} |");

        lines.Add(string.Empty);
        lines.Add("Change is the median, over paired rounds, of how much better (+) or worse (-) the head did than the base in the same round.");
        lines.Add("Difference is the same median in the unit of the scenario. A change beyond the tolerance whose difference is smaller than the floor of its scenario is not a verdict on the code: the verdict says so.");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    // values next to zero are the ones the floor is for: three decimals would print them all as 0.000
    private static string DescribeDifference(double difference)
    {
        if (double.IsNaN(difference))
            return "n/a";

        var text = Math.Abs(difference) >= 1
            ? Statistics.Format(difference)
            : difference.ToString("G2", System.Globalization.CultureInfo.InvariantCulture);
        return difference > 0 ? "+" + text : text;
    }

    private static string DescribeFloor(double minimumDifference)
        => minimumDifference > GateConfiguration.NoMinimumDifference
            ? minimumDifference.ToString("G3", System.Globalization.CultureInfo.InvariantCulture)
            : "none";

    private static string Describe(ScenarioComparison comparison)
        => comparison.Verdict == Verdict.BelowMinimumDifference
            ? $"ok: {(comparison.MedianChange < 0 ? "worse" : "better")} beyond the tolerance, but by less than the floor"
            : Describe(comparison.Verdict);

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
