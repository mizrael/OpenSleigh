using OpenSleigh.E2ETests.Harness.FaultInjection;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// every connection the hosts have to their database is closed under them, as a network blip or a failover does,
/// while the database itself stays available.
/// </summary>
public class DatabaseConnectionLossScenario(ITestOutputHelper console) : DatabaseInterruptionScenarioBase(console)
{
    private const int InvoicesBeforeTheConnectionsAreDropped = 3;

    // the stores whose server can close the connections of one database and leave the others alone, with one transport:
    // it is the database that goes away here, and the transport takes no part in it. Two hosts, each with its own pool
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq, Combinations.SqlServerRabbitMq], HostsCounts = [2])]
    public async Task hosts_work_again_without_a_restart_when_their_database_connections_are_dropped(string combination, int hostsCount)
    {
        var invoices = new Invoices();

        await RunScenarioAsync(combination, hostsCount, CreateHostSetup(invoices),
            async (run, cancellationToken) =>
            {
                await using var outage = DatabaseOutage.Of(run);

                // the hosts have connections open, in their pools, for the server to close
                for (var invoice = 0; invoice < InvoicesBeforeTheConnectionsAreDropped; invoice++)
                    Assert.True(await IssueAnInvoiceAndFollowItAsync(run, invoices, cancellationToken));

                await outage.DropConnectionsAsync(cancellationToken);

                await IssueInvoicesUntilTheyKeepCompletingAsync(run, invoices, cancellationToken);
            },
            AssertOnlyDatabaseFailuresWereDeadLetteredAsync);

        AssertEveryInvoiceIsAccountedFor(invoices);
    }
}
