namespace OpenSleigh.E2ETests.Harness;

/// <summary>one cell of the matrix: a scenario method, on a combination, with a number of hosts.</summary>
public sealed record MatrixCase(string Scenario, string Method, string Combination, int HostsCount)
{
    public override string ToString() => $"{Scenario}.{Method}({Combination}, {HostsCount})";
}

/// <param name="Id">a short name, to search the code and the reports for.</param>
/// <param name="Reason">what is wrong in the product, and what makes the case fail.</param>
/// <param name="Affects">the cases that cannot pass until the defect is fixed.</param>
public sealed record KnownDefect(string Id, string Reason, Func<MatrixCase, bool> Affects);

/// <summary>
/// the cases of the matrix that are not run because of a defect in the product, each with its reason.
/// This list is the only place where a case is taken out: an exclusion is removed by deleting its entry.
/// Set E2E_INCLUDE_KNOWN_DEFECTS=true to run the excluded cases anyway, for example to check a fix.
/// </summary>
public static class KnownDefects
{
    public const string IncludeVariable = "E2E_INCLUDE_KNOWN_DEFECTS";

    public static readonly IReadOnlyList<KnownDefect> Exclusions =
    [
        new KnownDefect(
            "cold-start-schema-race",
            "hosts that start together on an empty SQL database each run EnsureCreated, which checks and then creates: "
            + "the ones that lose the race fail to start ('database already exists' on SQL Server, a duplicate key on "
            + "pg_namespace on PostgreSQL). SagaDbSchemaInitializer serialises the contexts of one process only.",
            matrixCase => matrixCase.Scenario == nameof(Scenarios.ColdStartScenario)
                          && (Combinations.Uses(matrixCase.Combination, Combinations.SqlServer)
                              || Combinations.Uses(matrixCase.Combination, Combinations.PostgreSql))),
    ];

    public static bool AreIncluded
        => bool.TryParse(Environment.GetEnvironmentVariable(IncludeVariable), out var included) && included;

    public static KnownDefect? Find(MatrixCase matrixCase)
        => Exclusions.FirstOrDefault(defect => defect.Affects(matrixCase));
}
