using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the only host of a service is replaced while a saga waits for its timeout, as a deployment does:
/// the timeout is in the outbox, not in the memory of the host that published it.
/// </summary>
public class PaymentTimeoutAfterRestartScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private static readonly TimeSpan PaymentDueWithin = TimeSpan.FromSeconds(2);

    // a message the relay could not publish is read again when this has passed, on MongoDB
    private static readonly TimeSpan MongoOutboxClaim = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan OutboxPollingInterval = TimeSpan.FromMilliseconds(50);

    // not in memory: the outbox of a host goes away with it
    [Theory]
    [MatrixData(HostsCounts = [1], RequiresSharedInfrastructure = true)]
    public async Task an_order_expires_once_when_its_host_is_replaced_before_the_timeout_is_due(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var firstHost = new OrderPaymentJournal();
        var replacement = new OrderPaymentJournal();

        // stopping a host takes a second or two, mostly for a Kafka consumer to leave its group, and the timeout
        // can become due meanwhile: the first host cannot publish it, so that it is always the replacement that does,
        // however long the first one takes to stop
        var firstHostCannotPublishTheTimeout = new Fault("the timeout is left for the replacement to publish, on purpose.");
        firstHostCannotPublishTheTimeout.Start();

        var firstSetup = new HostSetup(OrderPaymentJournal.AddSagas)
        {
            ConfigureServices = services => firstHost.AddTo(services)
                .ExpireOutboxClaimsAfter(MongoOutboxClaim)
                .FailPublishingOf<PaymentTimedOut>(firstHostCannotPublishTheTimeout)
        };
        var replacementSetup = new HostSetup(OrderPaymentJournal.AddSagas)
        {
            ConfigureServices = services => replacement.AddTo(services).ExpireOutboxClaimsAfter(MongoOutboxClaim)
        };

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            var first = (await run.StartHostsAsync(hostsCount, firstSetup))[0];
            await first.PublishAsync(new PlaceOrder(correlationId, PaymentDueWithin), cancellationToken);

            // the timeout is stored with the order. The message that placed the order may still be in the outbox:
            // the relay removes a message after it has published it
            await run.WaitForSagaAsync<OrderPaymentSaga>(correlationId,
                stored => stored.ProcessedMessages.Count == 1 && !stored.IsLocked, cancellationToken);
            while (await run.CountOutboxMessagesAsync(cancellationToken) != 1)
                await Task.Delay(OutboxPollingInterval, cancellationToken);

            await first.StopAsync();
            Assert.Equal(0, firstHost.TimedOut.Count);

            await run.StartHostAsync(replacementSetup);

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
