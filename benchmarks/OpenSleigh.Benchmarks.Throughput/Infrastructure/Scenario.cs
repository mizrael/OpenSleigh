namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

internal static class Suites
{
    /// <summary>needs no infrastructure: fakes, in-memory providers, SQLite.</summary>
    public const string Local = "local";

    /// <summary>real infrastructure, sized to run on every pull request.</summary>
    public const string Gate = "gate";

    /// <summary>every real-infrastructure scenario.</summary>
    public const string Full = "full";
}

internal sealed record Scenario(
    string Name,
    IReadOnlyList<string> Suites,
    string Unit,
    bool HigherIsBetter,
    string Workload,
    Func<ScenarioContext, Task<double>> RunAsync);

internal sealed class ScenarioContext
{
    public ScenarioContext(Settings settings, ProblemLog problems)
    {
        Settings = settings;
        Problems = problems;
        RunId = Guid.NewGuid().ToString("N")[..12];
    }

    public Settings Settings { get; }

    public ProblemLog Problems { get; }

    /// <summary>unique per scenario run: used for database, exchange, topic and consumer group names.</summary>
    public string RunId { get; }
}

internal sealed record ScenarioResult(
    string Name,
    string Unit,
    bool HigherIsBetter,
    string Workload,
    IReadOnlyList<double> Samples,
    IReadOnlyList<string> Errors);

internal sealed record RunReport(
    string Variant,
    int Round,
    string Suite,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    string Machine,
    IReadOnlyList<ScenarioResult> Results);
