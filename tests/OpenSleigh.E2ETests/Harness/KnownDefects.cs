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
            "state-stored-after-failed-commit",
            "when the commit of a saga fails, the saga is released, and the release stores the state as the handler " +
            "left it (and whether it completed), without the messages it published and without the message being " +
            "recorded as processed: the handler runs again on a state it has already changed. " +
            "On every store, in-memory included (CircleCI job 1870: 7 cases out of 7, state changed once instead of not at all).",
            matrixCase => matrixCase.Scenario == "CommitFailureStateScenario"),

        new KnownDefect(
            "rabbitmq-connection-kept-after-reconnect",
            "a host that loses its connection to RabbitMQ ends up with one connection more every time: the connection " +
            "it had recovers by itself (automatic recovery is on) and keeps the consumers, while RabbitPersistentConnection " +
            "opens another one when it is told of the shutdown, and never closes the first. " +
            "CircleCI job 1894: 10 runs out of 10, two hosts with 2 connections had 4 after losing them once and 6 after twice.",
            matrixCase => matrixCase.Scenario == "RabbitMqConnectionLossScenario"
                          && matrixCase.Method == "hosts_have_as_many_connections_as_before_once_they_are_connected_again"),
    ];

    public static bool AreIncluded
        => bool.TryParse(Environment.GetEnvironmentVariable(IncludeVariable), out var included) && included;

    public static KnownDefect? Find(MatrixCase matrixCase)
        => Exclusions.FirstOrDefault(defect => defect.Affects(matrixCase));
}
