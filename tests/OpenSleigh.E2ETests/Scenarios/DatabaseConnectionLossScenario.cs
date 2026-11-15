using OpenSleigh.E2ETests.Harness.FaultInjection;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// every connection the hosts have to their database is closed under them, as a network blip or a failover does,
/// while the database itself stays available.
/// </summary>
public class DatabaseConnectionLossScenario(ITestOutputHelper console) : DatabaseInterruptionScenarioBase(console)
{
    // the stores whose server can close the connections of one database and leave the others alone, with one transport:
    // it is the database that goes away here, and the transport takes no part in it. Two hosts, each with its own pool
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq, Combinations.SqlServerRabbitMq], HostsCounts = [2])]
    public async Task every_host_works_again_without_a_restart_when_its_database_connections_are_dropped(string combination, int hostsCount)
    {
        var invoices = new Invoices();
        long connectionsDropped = 0;

        await RunScenarioAsync(combination, hostsCount, CreateHostSetup(invoices),
            async (run, cancellationToken) =>
            {
                await using var outage = DatabaseOutage.Of(run, Console);

                // an invoice through each host leaves connections open in its pool, for the server to close.
                // A pool can be emptied by something else in between, and nothing is then interrupted: it is done again
                async Task<long> DropOpenConnectionsAsync(CancellationToken dropping)
                {
                    while (true)
                    {
                        foreach (var host in run.Hosts)
                            Assert.True(await IssueAnInvoiceAndFollowItAsync(run, host, invoices, dropping));

                        var dropped = await outage.DropConnectionsAsync(dropping);
                        if (dropped > 0)
                            return dropped;
                    }
                }

                connectionsDropped = await run.WaitForAsync(
                    "the server to close connections that the hosts had open", DropOpenConnectionsAsync, cancellationToken, IsDatabaseFailure);

                await IssueInvoicesUntilEveryHostWorksAgainAsync(run, invoices, cancellationToken);
            },
            run => AssertEveryInvoiceIsAccountedForAsync(run, invoices));

        Assert.True(connectionsDropped >= 1);
    }
}
