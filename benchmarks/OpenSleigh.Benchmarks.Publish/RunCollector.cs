using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenSleigh.Benchmarks.Publish;

internal sealed record ScenarioSummary(
    string Name,
    string Unit,
    string Workload,
    IReadOnlyList<double> Head,
    IReadOnlyList<double> Base,
    double? Change,
    double? Difference);

internal sealed record BenchmarkRun(
    string Sha,
    string Url,
    string Subject,
    string Date,
    string? PreviousSha,
    int JobNumber,
    string JobUrl,
    int Rounds,
    string Machine,
    string MeasuredAt,
    IReadOnlyList<ScenarioSummary> Scenarios);

/// <summary>
/// Finds the benchmark jobs of a branch through the commit statuses CircleCI reports to GitHub,
/// and reads their results from the job artifacts through CircleCI's public API.
/// </summary>
internal sealed partial class RunCollector
{
    private const string GitHubApi = "https://api.github.com";
    private const string CircleCiApi = "https://circleci.com/api";
    private const string BaseSide = "base";
    private const string HeadSide = "head";

    private static readonly string[] FinishedStates = ["success", "failure", "error"];

    private readonly HttpClient _http;
    private readonly string _repository;
    private readonly string? _gitHubToken;

    public RunCollector(HttpClient http, string repository, string? gitHubToken)
    {
        _http = http;
        _repository = repository;
        _gitHubToken = gitHubToken;
    }

    public async Task<IReadOnlyList<BenchmarkRun>> CollectAsync(string branch, string statusContext, int maxCommits, int maxRuns)
    {
        var commits = await GetAsync($"{GitHubApi}/repos/{_repository}/commits?sha={Uri.EscapeDataString(branch)}&per_page={maxCommits}", gitHub: true);
        var runs = new List<BenchmarkRun>();

        foreach (var commit in commits.EnumerateArray())
        {
            if (runs.Count == maxRuns)
                break;

            var sha = commit.GetProperty("sha").GetString()!;
            try
            {
                var run = await ReadRunAsync(commit, sha, statusContext);
                if (run is not null)
                    runs.Add(run);
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
            {
                Console.Error.WriteLine($"skipping {sha[..7]}: {error.Message}");
            }
        }

        return runs;
    }

    private async Task<BenchmarkRun?> ReadRunAsync(JsonElement commit, string sha, string statusContext)
    {
        var job = await FindJobAsync(sha, statusContext);
        if (job is null)
            return null;

        var reports = await DownloadReportsAsync(job.Value.Number);
        var headReports = reports[HeadSide];
        if (headReports.Count == 0)
            return null;

        var firstReport = headReports[headReports.Keys.Min()];
        var details = commit.GetProperty("commit");
        var parents = commit.GetProperty("parents");

        return new BenchmarkRun(
            Sha: sha,
            Url: commit.GetProperty("html_url").GetString()!,
            Subject: details.GetProperty("message").GetString()!.Split('\n')[0],
            Date: details.GetProperty("committer").GetProperty("date").GetString()![..10],
            PreviousSha: parents.GetArrayLength() > 0 ? parents[0].GetProperty("sha").GetString() : null,
            JobNumber: job.Value.Number,
            JobUrl: job.Value.Url,
            Rounds: headReports.Count,
            Machine: firstReport.GetProperty("Machine").GetString() ?? string.Empty,
            MeasuredAt: firstReport.GetProperty("StartedAt").GetString()![..10],
            Scenarios: Summarise(reports));
    }

    private async Task<(int Number, string Url)?> FindJobAsync(string sha, string statusContext)
    {
        var statuses = await GetAsync($"{GitHubApi}/repos/{_repository}/commits/{sha}/statuses?per_page=100", gitHub: true);
        foreach (var status in statuses.EnumerateArray())
        {
            if (status.GetProperty("context").GetString() != statusContext ||
                !FinishedStates.Contains(status.GetProperty("state").GetString()))
                continue;

            var targetUrl = status.GetProperty("target_url").GetString() ?? string.Empty;
            var workflow = WorkflowId().Match(targetUrl);
            if (!workflow.Success)
                continue;

            var jobs = await GetAsync($"{CircleCiApi}/v2/workflow/{workflow.Groups[1].Value}/job", gitHub: false);
            foreach (var job in jobs.GetProperty("items").EnumerateArray())
                if (job.TryGetProperty("job_number", out var number) && number.ValueKind == JsonValueKind.Number)
                    return (number.GetInt32(), targetUrl);
        }

        return null;
    }

    private async Task<Dictionary<string, Dictionary<int, JsonElement>>> DownloadReportsAsync(int jobNumber)
    {
        var reports = new Dictionary<string, Dictionary<int, JsonElement>>
        {
            [BaseSide] = new(),
            [HeadSide] = new()
        };

        var artifacts = await GetAsync($"{CircleCiApi}/v1.1/project/github/{_repository}/{jobNumber}/artifacts", gitHub: false);
        foreach (var artifact in artifacts.EnumerateArray())
        {
            var file = ResultFile().Match(artifact.GetProperty("path").GetString() ?? string.Empty);
            if (!file.Success)
                continue;

            var report = await GetAsync(artifact.GetProperty("url").GetString()!, gitHub: false);
            reports[file.Groups[1].Value][int.Parse(file.Groups[2].Value)] = report;
        }

        return reports;
    }

    private static IReadOnlyList<ScenarioSummary> Summarise(Dictionary<string, Dictionary<int, JsonElement>> reports)
    {
        var definitions = new Dictionary<string, JsonElement>();
        foreach (var side in new[] { HeadSide, BaseSide })
            foreach (var report in reports[side].OrderBy(r => r.Key))
                foreach (var result in report.Value.GetProperty("Results").EnumerateArray())
                    definitions.TryAdd(result.GetProperty("Name").GetString()!, result);

        var summaries = new List<ScenarioSummary>(definitions.Count);
        foreach (var (name, definition) in definitions)
        {
            var head = ValuesByRound(reports[HeadSide], name);
            var baseline = ValuesByRound(reports[BaseSide], name);
            var higherIsBetter = definition.GetProperty("HigherIsBetter").GetBoolean();

            // base and head ran back to back in every round: the change is taken within a round, then the median over rounds
            var rounds = head.Keys.Intersect(baseline.Keys).ToArray();
            var changes = rounds
                .Select(round => higherIsBetter ? head[round] / baseline[round] - 1 : baseline[round] / head[round] - 1)
                .ToArray();

            // the same comparison in the unit of the scenario, with the same sign: what the gate holds against its floor
            var differences = rounds
                .Select(round => higherIsBetter ? head[round] - baseline[round] : baseline[round] - head[round])
                .ToArray();

            summaries.Add(new ScenarioSummary(
                name,
                definition.GetProperty("Unit").GetString()!,
                definition.GetProperty("Workload").GetString()!,
                head.OrderBy(v => v.Key).Select(v => v.Value).ToArray(),
                baseline.OrderBy(v => v.Key).Select(v => v.Value).ToArray(),
                changes.Length > 0 ? Numbers.Median(changes) : null,
                differences.Length > 0 ? Numbers.Median(differences) : null));
        }

        return summaries;
    }

    private static Dictionary<int, double> ValuesByRound(Dictionary<int, JsonElement> reports, string scenario)
    {
        var values = new Dictionary<int, double>();
        foreach (var (round, report) in reports)
            foreach (var result in report.GetProperty("Results").EnumerateArray())
            {
                if (result.GetProperty("Name").GetString() != scenario)
                    continue;

                var samples = result.GetProperty("Samples").EnumerateArray().Select(s => s.GetDouble()).ToArray();
                if (samples.Length > 0)
                    values[round] = Numbers.Median(samples);
            }
        return values;
    }

    private async Task<JsonElement> GetAsync(string url, bool gitHub)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // the token only goes to GitHub, and only to raise its rate limit
        if (gitHub && !string.IsNullOrEmpty(_gitHubToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _gitHubToken);

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(content);
        return document.RootElement.Clone();
    }

    [GeneratedRegex("/workflow/([0-9a-fA-F-]{36})")]
    private static partial Regex WorkflowId();

    [GeneratedRegex(@"benchmark-results/(base|head)-r(\d+)\.json$")]
    private static partial Regex ResultFile();
}

internal static class Numbers
{
    public static double Median(IReadOnlyCollection<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    public static string Format(double value)
    {
        var magnitude = Math.Abs(value);
        var format = magnitude >= 100 ? "N0" : magnitude >= 10 ? "N1" : magnitude >= 1 ? "N2" : "N3";
        return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }
}
