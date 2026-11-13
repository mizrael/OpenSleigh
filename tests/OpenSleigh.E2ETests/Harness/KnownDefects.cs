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
            Id: "rabbitmq-retry-is-delivered-to-every-service",
            Reason: "On RabbitMQ a message retried after a lock conflict is published to the retry exchange of its " +
                    "message type, to which the retry queue of every service is bound with the same routing key, " +
                    "and each retry queue hands it back to the exchange all services listen to. With two services, " +
                    "one retry delivers the message twice more to both: 3 deliveries each instead of 2 and 1 " +
                    "(every run in which both services were subscribed in time). The extra deliveries are stopped only by the processed-message check.",
            Affects: matrixCase => matrixCase.Scenario == nameof(Scenarios.RetryWithSeveralServicesScenario)
                                   && Combinations.Uses(matrixCase.Combination, Combinations.RabbitMq)),
    ];

    public static bool AreIncluded
        => bool.TryParse(Environment.GetEnvironmentVariable(IncludeVariable), out var included) && included;

    public static KnownDefect? Find(MatrixCase matrixCase)
        => Exclusions.FirstOrDefault(defect => defect.Affects(matrixCase));
}
