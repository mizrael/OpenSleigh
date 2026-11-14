using System.Collections.Concurrent;
using System.Diagnostics;
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
    public async Task hosts_work_again_without_a_restart_after_their_database_was_unavailable(string combination, int hostsCount)
    {
        var invoices = new Invoices();
        var failedPassesOfEachRelay = new ConcurrentDictionary<object, int>();
        var relaysThatWorkAgain = new ConcurrentDictionary<object, bool>();
        var databaseIsBack = false;
        Recovery? recovery = null;
        IReadOnlyCollection<ProcessingFailure> failures = [];
        TimeSpan outageLasted = default;

        var setup = CreateHostSetup(invoices,
            configureServices: services => services.ObserveRelayPasses((host, failed) =>
            {
                if (failed)
                    failedPassesOfEachRelay.AddOrUpdate(host, 1, (_, passes) => passes + 1);
                else if (Volatile.Read(ref databaseIsBack))
                    relaysThatWorkAgain.TryAdd(host, true);
            }));

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await using var outage = DatabaseOutage.Of(run);

                Assert.True(await IssueAnInvoiceAndFollowItAsync(run, invoices, cancellationToken));

                failedPassesOfEachRelay.Clear();
                var down = Stopwatch.StartNew();
                await outage.BeginAsync(cancellationToken);

                // nothing gets in while the database is away, and the relays keep finding it gone:
                // the outage lasts until each of them has, more than once
                var refusedBefore = invoices.Refused;
                while (invoices.Refused == refusedBefore || !EveryRelayHasFailed())
                {
                    var taken = await TryPublishAsync(run, new IssueInvoice(Guid.NewGuid().ToString()), cancellationToken);
                    Assert.False(taken, "the database took a message while it was away.");
                    invoices.WasRefused();
                }

                await outage.EndAsync(cancellationToken);
                Volatile.Write(ref databaseIsBack, true);
                outageLasted = down.Elapsed;

                recovery = await IssueInvoicesUntilTheyKeepCompletingAsync(run, invoices, cancellationToken);

                // the invoices can all have been relayed by one host: the relay of every host has to be reading again
                while (relaysThatWorkAgain.Count < hostsCount)
                    await Task.Delay(PauseAfterARefusal, cancellationToken);

                bool EveryRelayHasFailed()
                    => failedPassesOfEachRelay.Count == hostsCount
                       && failedPassesOfEachRelay.Values.All(passes => passes >= FailedRelayPassesOfEachHostToWaitFor);
            },
            async run =>
            {
                failures = FailuresThatDeadLetter(run);
                await AssertOnlyDatabaseFailuresWereDeadLetteredAsync(run);
            });

        Console.WriteLine(
            $"MEASURED database-outage combination={combination} outage-ms={outageLasted.TotalMilliseconds:F0} " +
            $"first-completed-ms={recovery!.TimeToFirstCompletedInvoice.TotalMilliseconds:F0} steady-ms={recovery.TimeToSteadyState.TotalMilliseconds:F0} " +
            $"invoices-tried={recovery.InvoicesTried} refused={invoices.Refused} failed-relay-passes={failedPassesOfEachRelay.Values.Sum()} dead-lettered={failures.Count}");

        AssertEveryInvoiceIsAccountedFor(invoices);
    }
}
