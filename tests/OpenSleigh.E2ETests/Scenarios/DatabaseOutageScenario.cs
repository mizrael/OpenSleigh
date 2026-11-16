using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the database of the hosts cannot be reached for a while, and then can again.
/// </summary>
public class DatabaseOutageScenario(ITestOutputHelper console) : DatabaseInterruptionScenarioBase(console)
{
    private const int FailedRelayPassesOfEachHostToWaitFor = 2;

    // the stores whose server can take one database away and leave the others alone, with one transport:
    // it is the database that goes away here, and the transport takes no part in it. Two hosts, each with its own relay
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq, Combinations.SqlServerRabbitMq], HostsCounts = [2])]
    public async Task every_host_works_again_without_a_restart_after_its_database_was_unavailable(string combination, int hostsCount)
    {
        var invoices = new Invoices();

        await RunScenarioAsync(combination, hostsCount, CreateHostSetup(invoices),
            async (run, cancellationToken) =>
            {
                await using var outage = DatabaseOutage.Of(run, Console);

                Assert.True(await IssueAnInvoiceAndFollowItAsync(run, run.AnyHost, invoices, cancellationToken));

                invoices.Activity.StartOver();
                await outage.BeginAsync(cancellationToken);

                bool EveryRelayHasFailed()
                    => run.Hosts.All(host => invoices.Activity.FailedRelayPasses(host) >= FailedRelayPassesOfEachHostToWaitFor);

                // no host gets anything in while the database is away, and the relays keep finding it gone:
                // the outage lasts until each of them has, more than once
                async Task PublishWhileTheDatabaseIsAwayAsync(CancellationToken publishing)
                {
                    do
                    {
                        foreach (var host in run.Hosts)
                        {
                            var taken = await TryPublishAsync(host, new IssueInvoice(Guid.NewGuid().ToString()), publishing);
                            Assert.False(taken, "the database took a message while it was away.");
                            invoices.WasRefused();
                        }
                    } while (!EveryRelayHasFailed());
                }

                await run.WaitForAsync(
                    $"every host to be refused a publish, and its relay to fail {FailedRelayPassesOfEachHostToWaitFor} times, while the database is away",
                    PublishWhileTheDatabaseIsAwayAsync, cancellationToken, IsDatabaseFailure);

                await outage.EndAsync(cancellationToken);

                await IssueInvoicesUntilEveryHostWorksAgainAsync(run, invoices, cancellationToken);
            },
            run => AssertEveryInvoiceIsAccountedForAsync(run, invoices));

        Assert.True(invoices.Refused >= hostsCount);
    }
}
