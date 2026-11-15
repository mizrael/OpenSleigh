using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Persistence.SQL;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the database goes away while a handler is running: the saga it was handling cannot be committed,
/// and cannot be released either.
/// </summary>
public class DatabaseOutageInFlightScenario(ITestOutputHelper console) : DatabaseInterruptionScenarioBase(console)
{
    // a saga that could not be released stays locked until its lock expires: after a minute by default,
    // which is longer than a scenario lasts
    private static readonly TimeSpan LockMaxDuration = TimeSpan.FromSeconds(3);

    // the stores whose server can take one database away and leave the others alone, with one transport:
    // it is the database that goes away here, and the transport takes no part in it. Two hosts: the message
    // that follows can reach the one that did not hold the lock
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq, Combinations.SqlServerRabbitMq], HostsCounts = [2])]
    public async Task a_saga_that_was_being_handled_when_its_database_went_away_is_not_locked_for_good(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var invoices = new Invoices();

        var setup = CreateHostSetup(invoices,
            services => services.AddSingleton(new SqlSagaStateRepositoryOptions(LockMaxDuration)));

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await using var outage = DatabaseOutage.Of(run);

                // the handler that sends the invoice waits, with its saga locked, until the database is gone
                var sending = invoices.Runs.HoldNext(handlerRun => handlerRun.MessageType == typeof(SendInvoice));
                await run.AnyHost.PublishNewAsync(new IssueInvoice(correlationId), cancellationToken);
                invoices.WasIssued(correlationId);
                await run.WaitForAsync("the invoice to reach the handler that sends it", sending.Entered, cancellationToken);

                await outage.BeginAsync(cancellationToken);
                sending.Release();

                // the handler is done and its saga cannot be committed: the host gives the message back
                await run.WaitUntilAsync("the message whose saga could not be committed to be given back",
                    () => invoices.TimesGivenBack(correlationId) >= 1, cancellationToken, IsInfrastructureFailure);

                await outage.EndAsync(cancellationToken);

                // the invoice is asked to be sent again, as whoever looks after the dead letters would,
                // until a host gets as far as handling it: one that still has a broken connection gives it back
                async Task SendAgainUntilCompletedAsync()
                {
                    var completed = false;
                    while (!completed)
                    {
                        var timesGivenBackBefore = invoices.TimesGivenBack(correlationId);
                        if (await TryPublishAsync(run, new SendInvoice(correlationId), cancellationToken))
                            completed = await FollowInvoiceAsync(run, invoices, correlationId, timesGivenBackBefore, cancellationToken);
                    }
                }

                await run.WaitForAsync("the invoice to be sent again and completed once the database is back",
                    SendAgainUntilCompletedAsync(), cancellationToken, IsInfrastructureFailure);
            },
            AssertOnlyInfrastructureFailuresWereDeadLetteredAsync);

        // the handler that was running, whose message was given back, and at least the one that completed the saga
        Assert.Equal([correlationId], invoices.Completed);
        Assert.True(invoices.TimesSent(correlationId) >= 2);
        Assert.True(invoices.TimesGivenBack(correlationId) >= 1);
    }
}
