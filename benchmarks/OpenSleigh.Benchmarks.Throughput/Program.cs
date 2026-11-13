using OpenSleigh.Benchmarks.Throughput.Infrastructure;
using OpenSleigh.Benchmarks.Throughput.Scenarios;
using System.Net.Sockets;
using System.Text.Json;

// Usage:
//   run     [--suite local|gate|full] [--scenario a,b] [--runs N] [--variant NAME] [--round N] [--out FILE.json]
//   compare --results DIR [--config gate.json] [--base-config gate.json] [--markdown FILE.md] [--no-fail]
//   wait    [--timeout SECONDS]
//   profile [--suite local|full] [--variant NAME] [--out FILE.md]
//   list

var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
var command = args.Length > 0 ? args[0] : "list";
var options = ParseOptions(args.Skip(1).ToArray());

switch (command)
{
    case "run":
        return await RunAsync();
    case "compare":
        return Compare();
    case "wait":
        return await WaitForInfrastructureAsync();
    case "profile":
        return await ProfileAsync();
    case "list":
        foreach (var scenario in Catalogue.All)
            Console.WriteLine($"{scenario.Name,-28} [{string.Join(",", scenario.Suites)}] {scenario.Unit}: {scenario.Workload}");
        return 0;
    default:
        Console.Error.WriteLine($"unknown command '{command}'");
        return 2;
}

async Task<int> RunAsync()
{
    var suite = options.GetValueOrDefault("suite", Suites.Local);
    var scenarios = Catalogue.Select(suite, options.GetValueOrDefault("scenario"));
    var runs = int.Parse(options.GetValueOrDefault("runs", "1")!);
    var variant = options.GetValueOrDefault("variant", "local")!;
    var round = int.Parse(options.GetValueOrDefault("round", "1")!);

    var report = await Runner.RunAsync(scenarios, suite!, variant, round, runs);
    var markdown = Runner.ToMarkdown(report);
    Console.WriteLine();
    Console.WriteLine(markdown);

    if (options.TryGetValue("out", out var outputPath) && !string.IsNullOrWhiteSpace(outputPath))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, jsonOptions));
        File.WriteAllText(Path.ChangeExtension(outputPath, ".md"), markdown);
    }

    // a scenario that fails is reported, not thrown: deciding what that means is the job of 'compare'
    return 0;
}

int Compare()
{
    if (!options.TryGetValue("results", out var resultsDirectory) || !Directory.Exists(resultsDirectory))
    {
        Console.Error.WriteLine("--results must point to a directory of run reports");
        return 2;
    }

    var configurationPath = options.GetValueOrDefault("config", Path.Combine(AppContext.BaseDirectory, "gate.json"))!;
    var configuration = File.Exists(configurationPath)
        ? JsonSerializer.Deserialize<GateConfiguration>(File.ReadAllText(configurationPath), jsonOptions)!
        : GateConfiguration.Default;

    var reports = Directory.GetFiles(resultsDirectory, "*.json")
        .Select(file => JsonSerializer.Deserialize<RunReport>(File.ReadAllText(file), jsonOptions)!)
        .ToArray();

    var comparisons = Comparison.Compare(reports, configuration);
    var markdown = Comparison.ToMarkdown(comparisons) + DescribeChangedSettings(configurationPath);
    Console.WriteLine(markdown);

    if (options.TryGetValue("markdown", out var markdownPath) && !string.IsNullOrWhiteSpace(markdownPath))
        File.WriteAllText(markdownPath, markdown);

    var failures = comparisons.Where(Comparison.Fails).Select(c => c.Name).ToArray();
    if (failures.Length == 0)
    {
        Console.WriteLine("Benchmark gate: no regression beyond tolerance.");
        return 0;
    }

    Console.WriteLine($"Benchmark gate: {failures.Length} scenario(s) got worse than the base beyond tolerance: {string.Join(", ", failures)}");
    return options.ContainsKey("no-fail") ? 0 : 1;
}

// the gate judges a branch by the settings of that branch, so that a branch can add a scenario with its tolerance.
// A branch can loosen them too: that is not prevented, it is said, where whoever reviews the result will read it
string DescribeChangedSettings(string configurationPath)
{
    if (!options.TryGetValue("base-config", out var baseConfigurationPath) || string.IsNullOrWhiteSpace(baseConfigurationPath))
        return string.Empty;

    static string Read(string path) => File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n").Trim() : string.Empty;

    return Read(configurationPath) == Read(baseConfigurationPath)
        ? string.Empty
        : Environment.NewLine + "**The settings of the gate (`benchmarks/gate.json`) are not those of the base branch: this comparison was judged by the changed ones.**" + Environment.NewLine;
}

async Task<int> ProfileAsync()
{
    var variant = options.GetValueOrDefault("variant", "local")!;
    var settings = Settings.FromEnvironment();
    var local = options.GetValueOrDefault("suite", Suites.Full) == Suites.Local;
    var lines = new List<string>();

    foreach (var (name, _, runAsync) in Catalogue.Profiles.Where(profile => profile.NeedsInfrastructure != local))
    {
        var problems = new ProblemLog();
        try
        {
            var profile = await runAsync(new ScenarioContext(settings, problems)).WaitAsync(TimeSpan.FromMinutes(4));
            lines.AddRange(Runner.ProfileToMarkdown(variant, name, profile));
        }
        catch (Exception ex)
        {
            lines.Add($"`{variant}` {name}: FAILED {ex.GetType().Name}: {ex.Message} {problems.Describe()}".Trim());
        }
        lines.Add(string.Empty);
    }

    var markdown = string.Join(Environment.NewLine, lines);
    Console.WriteLine(markdown);

    if (options.TryGetValue("out", out var outputPath) && !string.IsNullOrWhiteSpace(outputPath))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, markdown);
    }

    // a profile describes, it does not judge
    return 0;
}

async Task<int> WaitForInfrastructureAsync()
{
    var timeout = TimeSpan.FromSeconds(int.Parse(options.GetValueOrDefault("timeout", "180")!));
    var deadline = DateTime.UtcNow + timeout;
    var pending = Settings.FromEnvironment().Endpoints().ToList();

    while (pending.Count > 0 && DateTime.UtcNow < deadline)
    {
        foreach (var endpoint in pending.ToArray())
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(endpoint.Host, endpoint.Port).WaitAsync(TimeSpan.FromSeconds(2));
                Console.WriteLine($"{endpoint.Name} is accepting connections on {endpoint.Host}:{endpoint.Port}");
                pending.Remove(endpoint);
            }
            catch
            {
                // not up yet
            }
        }

        if (pending.Count > 0)
            await Task.Delay(TimeSpan.FromSeconds(2));
    }

    if (pending.Count == 0)
    {
        // a listening socket is not a ready server: give SQL Server and Kafka a moment to finish starting
        await Task.Delay(TimeSpan.FromSeconds(10));
        return 0;
    }

    Console.Error.WriteLine($"not reachable after {timeout.TotalSeconds:N0} s: {string.Join(", ", pending.Select(e => e.Name))}");
    return 1;
}

static Dictionary<string, string?> ParseOptions(string[] arguments)
{
    var parsed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < arguments.Length; i++)
    {
        if (!arguments[i].StartsWith("--"))
            continue;

        var name = arguments[i][2..];
        var hasValue = i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--");
        parsed[name] = hasValue ? arguments[++i] : null;
    }
    return parsed;
}
