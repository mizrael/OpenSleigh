using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit.Sdk;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// the rows of a scenario: every combination, with one host, a pair and a few; only one host
/// where hosts share nothing. A scenario method takes <c>(string combination, int hostsCount)</c>.
/// Cases listed in <see cref="KnownDefects"/> are left out; a scenario left with no case is reported as skipped.
/// Set E2E_COMBINATIONS to a comma separated list of combinations to run only those.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MatrixDataAttribute : DataAttribute
{
    public const string CombinationsVariable = "E2E_COMBINATIONS";

    private const int OneHost = 1;
    private static readonly int[] DefaultHostsCounts = [OneHost, 2, 5];

    private const string NotRun = "not-run";
    private static readonly char[] PathSeparators = ['/', '\\'];

    private readonly string _scenario;
    private readonly string _method;

    /// <summary>
    /// the parameters are filled in by the compiler: a scenario class is in a file of the same name.
    /// </summary>
    public MatrixDataAttribute(
        [CallerFilePath] string scenarioFile = "",
        [CallerMemberName] string method = "")
    {
        _scenario = Path.GetFileNameWithoutExtension(scenarioFile.Split(PathSeparators)[^1]);
        _method = method;
    }

    /// <summary>the combinations the scenario runs on. All of them when not set.</summary>
    public string[]? Only { get; set; }

    /// <summary>
    /// the numbers of hosts the scenario runs with. Where hosts share nothing it runs with one host,
    /// or not at all when one host is not among them.
    /// </summary>
    public int[]? HostsCounts { get; set; }

    /// <summary>
    /// true for a scenario with hosts that have to reach each other, such as two services:
    /// it does not run where hosts share nothing.
    /// </summary>
    public bool RequiresSharedInfrastructure { get; set; }

    /// <summary>
    /// set when no case of the scenario is run: E2E_COMBINATIONS asks for none of its combinations,
    /// or all of those it asks for are excluded by a known defect. The scenario is then reported as skipped,
    /// with the reason, instead of failing for having no rows.
    /// </summary>
    /// <exception cref="InvalidOperationException">the scenario has no case at all: its matrix is wrong.</exception>
    public override string? Skip
    {
        get
        {
            if (CasesToRun().Any())
                return base.Skip;

            var defect = RequestedCases().Select(KnownDefects.Find).FirstOrDefault(found => found is not null);
            return defect is not null
                ? $"known defect '{defect.Id}': {defect.Reason}"
                : $"{CombinationsVariable} asks for none of the combinations this scenario runs on.";
        }
        set => base.Skip = value;
    }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        var rows = CasesToRun()
            .Select(matrixCase => new object[] { matrixCase.Combination, matrixCase.HostsCount })
            .ToArray();

        // a theory needs a row to be reported at all, skipped or not
        return rows.Length > 0 ? rows : [[NotRun, 0]];
    }

    /// <summary>every case of the scenario, whatever the environment asks for and known defects included.</summary>
    /// <exception cref="InvalidOperationException">a name in <see cref="Only"/> is not a combination,
    /// or the scenario has no case: it would be skipped for ever without anybody noticing.</exception>
    public IReadOnlyList<MatrixCase> AllCases()
    {
        var combinations = Only is null ? Combinations.All : Only.Select(GetCombination).ToArray();

        var cases = (from combination in combinations
                     where combination.HostsShareInfrastructure || !RequiresSharedInfrastructure
                     from hostsCount in HostsCountsOn(combination)
                     select new MatrixCase(_scenario, _method, combination.Name, hostsCount)).ToArray();

        return cases.Length > 0
            ? cases
            : throw new InvalidOperationException($"the matrix of {_scenario}.{_method} has no case.");
    }

    private Combination GetCombination(string name)
    {
        try
        {
            return Combinations.Get(name);
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException($"the matrix of {_scenario}.{_method} is wrong: {e.Message}", e);
        }
    }

    private IEnumerable<int> HostsCountsOn(Combination combination)
    {
        var hostsCounts = HostsCounts ?? DefaultHostsCounts;
        return combination.HostsShareInfrastructure
            ? hostsCounts
            : hostsCounts.Where(hostsCount => hostsCount == OneHost);
    }

    private IEnumerable<MatrixCase> RequestedCases()
    {
        var requested = RequestedCombinations();
        return AllCases().Where(matrixCase => requested is null || requested.Contains(matrixCase.Combination));
    }

    private IEnumerable<MatrixCase> CasesToRun()
        => RequestedCases().Where(matrixCase => KnownDefects.AreIncluded || KnownDefects.Find(matrixCase) is null);

    private static HashSet<string>? RequestedCombinations()
    {
        var requested = Environment.GetEnvironmentVariable(CombinationsVariable);
        if (string.IsNullOrWhiteSpace(requested))
            return null;

        var names = requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var name in names)
            Combinations.Get(name);

        return names.ToHashSet();
    }
}
