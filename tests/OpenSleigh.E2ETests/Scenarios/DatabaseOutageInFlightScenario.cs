using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Persistence.SQL;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the database goes away while a handler is running: the saga it was handling cannot be committed, and cannot be
/// released either, so it stays locked for as long as its lock lasts. Nobody handles it before that,
/// and somebody can afterwards.
/// </summary>
public class DatabaseOutageInFlightScenario(ITestOutputHelper console) : DatabaseInterruptionScenarioBase(console)
{
    // a minute by default, which is longer than a scenario lasts
    private static readonly TimeSpan LockMaxDuration = TimeSpan.FromSeconds(3);

    // the lock is written just before the handler that holds it is timed, with the clock of the machine;
    // the handler runs are timed with a stopwatch. A lock that was not honoured at all is short by seconds
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMilliseconds(100);

    // the stores whose server can take one database away and leave the others alone, with one transport:
    // it is the database that goes away here, and the transport takes no part in it. Two hosts: the message
    // that follows can reach the one that did not hold the lock
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq, Combinations.SqlServerRabbitMq], HostsCounts = [2])]
    public async Task a_saga_that_was_being_handled_when_its_database_went_away_stays_locked_until_its_lock_expires_and_no_longer(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var invoices = new Invoices();

        var setup = CreateHostSetup(invoices,
            services => services.AddSingleton(new SqlSagaStateRepositoryOptions(LockMaxDuration)));

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await using var outage = DatabaseOutage.Of(run, Console);

                // the handler that sends the invoice waits, with its saga locked, until the database is gone
                var sending = invoices.Runs.HoldNext(handlerRun => handlerRun.MessageType == typeof(SendInvoice));
                await run.AnyHost.PublishNewAsync(new IssueInvoice(correlationId), cancellationToken);
                invoices.WasIssued(correlationId);
                await run.WaitForAsync("the invoice to reach the handler that sends it", sending.Entered, cancellationToken);

                await outage.BeginAsync(cancellationToken);
                sending.Release();

                // the handler is done and its saga cannot be committed: the host gives the message back
                await run.WaitUntilAsync("the message whose saga could not be committed to be given back",
                    () => TimesGivenBack(run, correlationId) >= 1, cancellationToken, IsDatabaseFailure);

                await outage.EndAsync(cancellationToken);

                // the invoice is asked to be sent again, as whoever looks after the dead letters would,
                // until a host gets as far as handling it: one that still has a broken connection gives it back
                async Task SendAgainUntilCompletedAsync(CancellationToken sendingAgain)
                {
                    var completed = false;
                    for (var turn = 0; !completed; turn++)
                    {
                        var timesGivenBackBefore = TimesGivenBack(run, correlationId);
                        if (await TryPublishAsync(run.Hosts[turn % run.Hosts.Count], new SendInvoice(correlationId), sendingAgain))
                            completed = await FollowInvoiceAsync(run, invoices, correlationId, timesGivenBackBefore, sendingAgain);
                    }
                }

                await run.WaitForAsync("the invoice to be sent again and completed once the database is back",
                    SendAgainUntilCompletedAsync, cancellationToken, IsDatabaseFailure);
            },
            AssertOnlyDatabaseFailuresWereDeadLetteredAsync);

        Assert.Equal([correlationId], invoices.Completed);

        // the handler that was running when the database went away, and the one that completed the saga. Delivery is
        // at least once: a host whose commit failed on a connection that was still broken ran it in between
        var sends = invoices.SendsOf(correlationId);
        Assert.True(sends.Count >= 2);

        // whether a host found the saga locked in the meantime depends on how long the database took to come back.
        // What holds either way: nobody handled the saga again while the lock its first handler left was still valid
        var handledAgainAfter = Stopwatch.GetElapsedTime(sends[0].At, sends[1].At);
        Assert.True(handledAgainAfter >= LockMaxDuration - ClockTolerance,
            $"the saga was handled again {handledAgainAfter.TotalMilliseconds} ms after the handler that left it locked: its lock lasts {LockMaxDuration.TotalMilliseconds} ms.");
    }
}
