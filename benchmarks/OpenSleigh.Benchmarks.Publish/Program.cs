using OpenSleigh.Benchmarks.Publish;
using System.Text.Json;

// Builds the benchmark results shown on the docs site.
//
// The benchmark_develop CircleCI job measures every commit that lands on the base branch and stores its results
// as job artifacts. This tool finds those jobs through the commit statuses CircleCI reports to GitHub, downloads
// the artifacts through CircleCI's public API and writes the Markdown fragment that docs/benchmarks.md includes.
// It needs no CircleCI credentials; GITHUB_TOKEN is used when present, for GitHub's rate limit only.
//
// Nothing is committed back: the fragment is regenerated every time the site is built. When no run can be read,
// the fragment already in the repository is left as it is and the exit code is still 0, so the site keeps building.
//
// Options: --repo owner/name  --branch develop  --context "ci/circleci: benchmark_develop"
//          --gate-config benchmarks/gate.json  --output docs/_includes/benchmark-results.md
//          --max-commits 30  --max-runs 8

const string DefaultRepository = "mizrael/OpenSleigh";
const string DefaultContext = "ci/circleci: benchmark_develop";

var options = ParseOptions(args);
var repository = options.GetValueOrDefault("repo") ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? DefaultRepository;
var branch = options.GetValueOrDefault("branch") ?? "develop";
var statusContext = options.GetValueOrDefault("context") ?? DefaultContext;
var gateConfigPath = options.GetValueOrDefault("gate-config") ?? Path.Combine("benchmarks", "gate.json");
var outputPath = options.GetValueOrDefault("output") ?? Path.Combine("docs", "_includes", "benchmark-results.md");
var maxCommits = int.Parse(options.GetValueOrDefault("max-commits") ?? "30");
var maxRuns = int.Parse(options.GetValueOrDefault("max-runs") ?? "8");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("opensleigh-benchmarks-publish");

IReadOnlyList<BenchmarkRun> runs;
try
{
    var collector = new RunCollector(http, repository, Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
    runs = await collector.CollectAsync(branch, statusContext, maxCommits, maxRuns);
}
catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
{
    Console.Error.WriteLine($"could not read the benchmark runs: {error.Message}");
    runs = [];
}

if (runs.Count == 0)
{
    Console.WriteLine("no benchmark run found: the results fragment is left unchanged");
    return 0;
}

var (tolerances, defaultTolerance) = ReadTolerances(gateConfigPath);
File.WriteAllText(outputPath, ResultsPage.Render(runs, repository, branch, tolerances, defaultTolerance));
Console.WriteLine($"wrote {outputPath} from {runs.Count} run(s), latest {runs[0].Sha[..7]}");
return 0;

static (Dictionary<string, double> Tolerances, double DefaultTolerance) ReadTolerances(string gateConfigPath)
{
    const double fallbackTolerance = 0.30;
    var tolerances = new Dictionary<string, double>();
    if (!File.Exists(gateConfigPath))
        return (tolerances, fallbackTolerance);

    using var gate = JsonDocument.Parse(File.ReadAllText(gateConfigPath));
    var defaultTolerance = gate.RootElement.TryGetProperty("defaultTolerance", out var value) ? value.GetDouble() : fallbackTolerance;

    if (gate.RootElement.TryGetProperty("scenarios", out var scenarios) && scenarios.ValueKind == JsonValueKind.Object)
        foreach (var scenario in scenarios.EnumerateObject())
            if (scenario.Value.TryGetProperty("tolerance", out var tolerance))
                tolerances[scenario.Name] = tolerance.GetDouble();

    return (tolerances, defaultTolerance);
}

static Dictionary<string, string?> ParseOptions(string[] arguments)
{
    var parsed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < arguments.Length - 1; i++)
        if (arguments[i].StartsWith("--"))
            parsed[arguments[i][2..]] = arguments[++i];
    return parsed;
}
