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
        IReadOnlyDictionary<string, double> tolerances,
        double defaultTolerance)
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
        {
            var tolerance = tolerances.GetValueOrDefault(scenario.Name, defaultTolerance);
            page.AppendLine($"| {scenario.Name} | {scenario.Unit} | {Spread(scenario.Head)} | {Spread(scenario.Base)} | {DescribeChange(scenario.Change, tolerance)} |");
        }

        page.AppendLine();
        page.AppendLine(
            "Values are the median over the rounds, with the lowest and highest round in parentheses. " +
            "Change is how much better (+) or worse (-) this commit did than the previous one, taken round by round. " +
            $"A change inside the tolerance of the benchmark gate ({Percent(defaultTolerance)} unless a scenario sets its own) is within noise.");

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

    private static string DescribeChange(double? change, double tolerance)
    {
        if (change is null)
            return "n/a";

        var percent = Math.Round(change.Value * 100, MidpointRounding.AwayFromZero);
        var text = percent == 0 ? "0%" : percent.ToString("+0;-0", CultureInfo.InvariantCulture) + "%";
        if (change > tolerance)
            return $"{text} (faster)";
        if (change < -tolerance)
            return $"{text} (slower)";
        return $"{text} (within noise)";
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
