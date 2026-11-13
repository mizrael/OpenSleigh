using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenSleigh.Benchmarks.Publish;

/// <summary>
/// Renders benchmark runs as the Markdown fragment the docs page includes.
/// </summary>
internal static partial class ResultsPage
{
    public static string Render(
        IReadOnlyList<BenchmarkRun> runs,
        string repository,
        string branch,
        GateSettings gate)
    {
        var latest = runs[0];
        var page = new StringBuilder();

        page.AppendLine("## Latest run");
        page.AppendLine();
        page.AppendLine($"- Commit: [`{latest.Sha[..7]}`]({latest.Url}) on `{branch}`, {latest.Date}: {PlainText(latest.Subject)}");
        page.AppendLine($"- Measured: {latest.MeasuredAt}, {latest.Rounds} rounds, [CircleCI job {latest.JobNumber}]({latest.JobUrl})");
        page.AppendLine($"- Runner: {PlainText(latest.Machine)}");
        if (latest.PreviousSha is not null)
            page.AppendLine($"- Previous commit: [`{latest.PreviousSha[..7]}`](https://github.com/{repository}/commit/{latest.PreviousSha}), measured again in the same job");

        page.AppendLine();
        page.AppendLine("| Scenario | Unit | This commit | Previous commit | Change |");
        page.AppendLine("|---|---|---:|---:|---|");
        foreach (var scenario in latest.Scenarios)
            page.AppendLine($"| {scenario.Name} | {scenario.Unit} | {Spread(scenario.Head)} | {Spread(scenario.Base)} | {DescribeChange(scenario, gate)} |");

        page.AppendLine();
        page.AppendLine(
            "Values are the median over the rounds, with the lowest and highest round in parentheses. " +
            "Change is how much better (+) or worse (-) this commit did than the previous one, taken round by round. " +
            $"A change inside the tolerance of the benchmark gate ({Percent(gate.DefaultTolerance)} unless a scenario sets its own) is within noise, " +
            "and so is a larger one on a scenario that measures microseconds, when the difference is below the absolute floor the gate has for it.");

        page.AppendLine();
        page.AppendLine("## What each scenario does");
        page.AppendLine();
        page.AppendLine("| Scenario | Workload |");
        page.AppendLine("|---|---|");
        foreach (var scenario in latest.Scenarios)
            page.AppendLine($"| {scenario.Name} | {PlainText(scenario.Workload)} |");

        if (runs.Count > 1)
            AppendHistory(page, runs);

        page.AppendLine();
        page.AppendLine($"Page generated {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC from {runs.Count} run(s).");
        return page.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendHistory(StringBuilder page, IReadOnlyList<BenchmarkRun> runs)
    {
        page.AppendLine();
        page.AppendLine("## History");
        page.AppendLine();
        page.AppendLine("Median per commit, newest first. Every column comes from a different CI runner, so small differences between columns mean nothing.");
        page.AppendLine();
        page.AppendLine("| Scenario | Unit | " + string.Join(" | ", runs.Select(run => $"[`{run.Sha[..7]}`]({run.Url})<br>{run.Date}")) + " |");
        page.AppendLine("|---|---|" + string.Concat(Enumerable.Repeat("---:|", runs.Count)));

        foreach (var scenario in runs[0].Scenarios)
        {
            var cells = runs.Select(run =>
            {
                var match = run.Scenarios.FirstOrDefault(s => s.Name == scenario.Name);
                return match is { Head.Count: > 0 } ? Numbers.Format(Numbers.Median(match.Head)) : "n/a";
            });
            page.AppendLine($"| {scenario.Name} | {scenario.Unit} | {string.Join(" | ", cells)} |");
        }
    }

    private static string Spread(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return "n/a";

        var median = Numbers.Format(Numbers.Median(values));
        return values.Count == 1 ? median : $"{median} ({Numbers.Format(values.Min())} to {Numbers.Format(values.Max())})";
    }

    private static string DescribeChange(ScenarioSummary scenario, GateSettings gate)
    {
        if (scenario.Change is not { } change)
            return "n/a";

        var percent = Math.Round(change * 100, MidpointRounding.AwayFromZero);
        var text = percent == 0 ? "0%" : percent.ToString("+0;-0", CultureInfo.InvariantCulture) + "%";
        if (gate.IsNoise(scenario.Name, change, scenario.Difference))
            return $"{text} (within noise)";

        return change > 0 ? $"{text} (faster)" : $"{text} (slower)";
    }

    private static string Percent(double fraction) => fraction.ToString("0%", CultureInfo.InvariantCulture);

    // commit subjects and workloads end up inside a table that Liquid and Markdown both process
    private static string PlainText(string value)
        => Whitespace().Replace(MarkupCharacters().Replace(value, " "), " ").Trim();

    [GeneratedRegex(@"[{}|<>`\[\]*_]")]
    private static partial Regex MarkupCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
