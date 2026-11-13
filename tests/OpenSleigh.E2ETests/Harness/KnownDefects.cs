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

    /// <summary>
    /// not an exclusion: UseMongoPersistence registers no ITransactionManager, which the outbox relay needs,
    /// so a host on MongoDB relays nothing. MongoStore registers one that does nothing, to be able to run at all.
    /// </summary>
    public const string MongoRegistersNoTransactionManager = "mongo-registers-no-transaction-manager";

    public static readonly IReadOnlyList<KnownDefect> Exclusions =
    [
        new KnownDefect(
            Id: "message-for-a-saga-not-stored-yet-is-dead-lettered",
            Reason: "A message that starts two sagas is handled by one saga after the other, each committed on its own. " +
                    "What the first saga publishes can reach another host before the second saga is stored: " +
                    "that host fails with \"unable to locate state for Saga\" and the message is dead-lettered, " +
                    "after the first saga has already handled it. Needs two hosts or more; seen in 4 of 40 runs " +
                    "on SQL Server with RabbitMQ, and possible wherever hosts share a transport.",
            Affects: matrixCase => matrixCase.Scenario == nameof(Scenarios.MultipleSagasSameMessagesScenario)
                                   && matrixCase.HostsCount > 1),
    ];

    public static bool AreIncluded
        => bool.TryParse(Environment.GetEnvironmentVariable(IncludeVariable), out var included) && included;

    public static KnownDefect? Find(MatrixCase matrixCase)
        => Exclusions.FirstOrDefault(defect => defect.Affects(matrixCase));
}
