using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the only host of a service is replaced while a saga waits for its timeout, as a deployment does:
/// the timeout is in the outbox, not in the memory of the host that published it.
/// </summary>
public class PaymentTimeoutAfterRestartScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // the first host has to be stopped before the timeout is due, or it would handle it itself:
    // stopping a host takes a second or two, mostly for a Kafka consumer to leave its group
    private static readonly TimeSpan PaymentDueWithin = TimeSpan.FromSeconds(6);

    // not in memory: the outbox of a host goes away with it
    [Theory]
    [MatrixData(HostsCounts = [1], RequiresSharedInfrastructure = true)]
    public async Task an_order_expires_once_when_its_host_is_replaced_before_the_timeout_is_due(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var firstHost = new OrderPaymentJournal();
        var replacement = new OrderPaymentJournal();

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            var first = (await run.StartHostsAsync(hostsCount, firstHost.CreateHostSetup()))[0];
            await first.PublishAsync(new PlaceOrder(correlationId, PaymentDueWithin), cancellationToken);

            // the timeout is stored with the order
            await run.WaitForSagaAsync<OrderPaymentSaga>(correlationId,
                stored => stored.ProcessedMessages.Count == 1 && !stored.IsLocked, cancellationToken);
            Assert.Equal(1, await run.CountOutboxMessagesAsync(cancellationToken));

            await first.StopAsync();
            Assert.Equal(0, firstHost.TimedOut.Count);

            await run.StartHostAsync(replacement.CreateHostSetup());

            await replacement.TimedOut.Reached.WaitAsync(cancellationToken);
            await replacement.ExpiredOrderHandled.Reached.WaitAsync(cancellationToken);
            await run.WaitForSagaAsync<OrderPaymentSaga>(correlationId, stored => stored.IsCompleted, cancellationToken);
        });

        Assert.Equal(0, firstHost.TimedOut.Count);
        Assert.Equal(0, firstHost.ExpiredOrderHandled.Count);
        Assert.Equal(1, replacement.TimedOut.Count);
        Assert.Equal(1, replacement.ExpiredOrderHandled.Count);
        OrderPaymentJournal.AssertTimedOutOnceWhenDue(firstHost, replacement, PaymentDueWithin);
    }
}
