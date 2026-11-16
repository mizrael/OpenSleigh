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
    private const int MessagesOfAnOrderThatExpires = 2;
    private static readonly TimeSpan PaymentDueWithin = TimeSpan.FromSeconds(2);

    // a message the relay could not publish is read again when this has passed, on MongoDB
    private static readonly TimeSpan MongoOutboxClaim = TimeSpan.FromSeconds(2);

    // what keeps the timeout while no host runs is the outbox of the store: every store runs it, and one combination
    // has Kafka. Not in memory: the outbox of a host goes away with it
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [1])]
    public async Task an_order_expires_once_when_its_host_is_replaced_while_the_timeout_is_pending(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var handledByFirstHost = new HandlerRuns();
        var handledByReplacement = new HandlerRuns();

        // stopping a host takes a second or two, and the timeout can become due meanwhile: the first host cannot
        // publish it, so that it is always the replacement that does, however long the first one takes to stop
        var firstHostCannotPublishTheTimeout = new Fault("the timeout is left for the replacement to publish, on purpose.");
        firstHostCannotPublishTheTimeout.Start();

        var firstSetup = PaymentTimeoutWorkload.HostSetup(services => services
            .RecordHandlerRuns(handledByFirstHost)
            .ExpireOutboxClaimsAfter(MongoOutboxClaim)
            .FailPublishingOf<PaymentTimedOut>(firstHostCannotPublishTheTimeout));
        var replacementSetup = PaymentTimeoutWorkload.HostSetup(services => services
            .RecordHandlerRuns(handledByReplacement)
            .ExpireOutboxClaimsAfter(MongoOutboxClaim));

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            var first = (await run.StartHostsAsync(hostsCount, firstSetup))[0];
            await first.PublishNewAsync(new PlaceOrder(correlationId, PaymentDueWithin), cancellationToken);

            // the timeout is stored with the order
            await run.WaitForReleasedSagaAsync<OrderPaymentSaga>(correlationId, handledMessages: 1, cancellationToken);
            await first.StopAsync();

            await run.StartHostAsync(replacementSetup);

            var order = await run.WaitForReleasedSagaAsync<OrderPaymentSaga>(correlationId, MessagesOfAnOrderThatExpires, cancellationToken);
            Assert.True(order.IsCompleted);
            await run.WaitUntilAsync("the message the timeout published to be handled",
                () => handledByReplacement.RunsOf<OrderExpired>().Count >= 1, cancellationToken);
        });

        // also after the observation window: the first host handled the order and nothing else,
        // the replacement the timeout and what it published, once
        var placed = Assert.Single(handledByFirstHost.Runs);
        Assert.Equal(typeof(PlaceOrder), placed.MessageType);

        var timedOut = Assert.Single(handledByReplacement.RunsOf<PaymentTimedOut>());
        Assert.Single(handledByReplacement.RunsOf<OrderExpired>());
        Assert.Equal(2, handledByReplacement.Runs.Count);
        PaymentTimeoutWorkload.AssertHandledWhenDue(placed, timedOut, PaymentDueWithin);
    }
}
