using System.Reflection;
using Xunit.Sdk;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// the rows of a scenario: every combination, with one host, a pair and a few; only one host
/// where hosts share nothing. A scenario method takes <c>(string combination, int hostsCount)</c>.
/// Cases listed in <see cref="KnownDefects"/> are left out.
/// Set E2E_COMBINATIONS to a comma separated list of combinations to run only those.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MatrixDataAttribute : DataAttribute
{
    public const string CombinationsVariable = "E2E_COMBINATIONS";

    private static readonly int[] DefaultHostsCounts = [1, 2, 5];
    private static readonly int[] SingleHost = [1];

    private const string NotRequested = "not-requested";

    /// <summary>the combinations the scenario runs on. All of them when not set.</summary>
    public string[]? Only { get; set; }

    /// <summary>the numbers of hosts the scenario runs with, where hosts share their infrastructure.</summary>
    public int[]? HostsCounts { get; set; }

    /// <summary>
    /// set when E2E_COMBINATIONS asks for none of the combinations of the scenario:
    /// the scenario is reported as skipped, instead of failing for having no rows.
    /// </summary>
    public override string? Skip
    {
        get => IsRequested()
            ? base.Skip
            : $"{CombinationsVariable} asks for none of the combinations this scenario runs on.";
        set => base.Skip = value;
    }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        // a theory needs a row to be reported at all, skipped or not
        if (!IsRequested())
            return [[NotRequested, 0]];

        var requested = RequestedCombinations();

        return GetCases(testMethod)
            .Where(matrixCase => requested is null || requested.Contains(matrixCase.Combination))
            .Where(matrixCase => KnownDefects.AreIncluded || KnownDefects.Find(matrixCase) is null)
            .Select(matrixCase => new object[] { matrixCase.Combination, matrixCase.HostsCount });
    }

    private bool IsRequested()
    {
        var requested = RequestedCombinations();
        return requested is null
               || Combinations.All.Any(combination => requested.Contains(combination.Name)
                                                      && (Only is null || Only.Contains(combination.Name)));
    }

    /// <summary>every case of the scenario, whatever the environment asks for and known defects included.</summary>
    public IEnumerable<MatrixCase> GetCases(MethodInfo testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);

        // a scenario can be declared on a base class: the class under test is the one the method was found through
        var scenario = (testMethod.ReflectedType ?? testMethod.DeclaringType)?.Name
            ?? throw new InvalidOperationException($"unable to tell which class '{testMethod.Name}' is run for.");

        return from combination in Combinations.All
               where Only is null || Only.Contains(combination.Name)
               from hostsCount in combination.HostsShareInfrastructure ? HostsCounts ?? DefaultHostsCounts : SingleHost
               select new MatrixCase(scenario, testMethod.Name, combination.Name, hostsCount);
    }

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
