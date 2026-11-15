using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the reply a saga was waiting for arrives before its timeout is due: the timeout has nothing left to do.
/// </summary>
public class PaymentBeforeTimeoutScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int MessagesOfAnOrderThatIsPaid = 2;
    private static readonly TimeSpan PaymentDueWithin = TimeSpan.FromSeconds(2);

    // a message the relay could not publish is read again when this has passed, on MongoDB
    private static readonly TimeSpan MongoOutboxClaim = TimeSpan.FromSeconds(2);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task the_timeout_of_an_order_paid_in_time_is_delivered_and_not_handled(string combination, int hostsCount)
        => PayAnOrderInTimeAsync(combination, hostsCount);

    // the timeout is one row in an outbox that every host relays, and whichever host receives it has to leave it alone.
    // That is up to the store, which says the saga is completed: every store runs it, and one combination has Kafka
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [3])]
    public Task the_timeout_of_an_order_paid_in_time_is_delivered_and_not_handled_with_several_hosts(string combination, int hostsCount)
        => PayAnOrderInTimeAsync(combination, hostsCount);

    private async Task PayAnOrderInTimeAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns();

        // the timeout of a completed saga reaches no handler: the hosts tell when they are done with it
        var timeoutDelivered = new Expectation(1);

        // the payment is published as soon as the order is stored, long before the timeout is due. Should the machine
        // be slow enough for the timeout to become due first, it does not leave the outbox until the order is paid:
        // which of the two arrives first is not left to the clock
        var timeoutIsHeld = new Fault("the timeout stays in the outbox until the order is paid, on purpose.");
        timeoutIsHeld.Start();

        var setup = PaymentTimeoutWorkload.HostSetup(services => services
            .RecordHandlerRuns(runs)
            .ObserveDeliveriesOf<PaymentTimedOut>(timeoutDelivered)
            .ExpireOutboxClaimsAfter(MongoOutboxClaim)
            .FailPublishingOf<PaymentTimedOut>(timeoutIsHeld));

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishNewAsync(new PlaceOrder(correlationId, PaymentDueWithin), cancellationToken);

                // a payment that arrives before its order is stored has no saga to go to
                await run.WaitForReleasedSagaAsync<OrderPaymentSaga>(correlationId, handledMessages: 1, cancellationToken);
                await run.AnyHost.PublishNewAsync(new PaymentReceived(correlationId), cancellationToken);

                var order = await run.WaitForReleasedSagaAsync<OrderPaymentSaga>(correlationId, MessagesOfAnOrderThatIsPaid, cancellationToken);
                Assert.True(order.IsCompleted);
                Assert.Equal(0, timeoutDelivered.Count);
                timeoutIsHeld.Stop();

                // the timeout is still in the outbox, and becomes due: a host receives it and leaves it at that.
                // The in-memory outbox does not count a message that is not due, so its being empty would prove nothing
                await run.WaitForAsync("the timeout of the paid order to be delivered", timeoutDelivered.Reached, cancellationToken);

                var state = Assert.IsType<OrderPaymentState>(order.StateData);
                Assert.True(state.Paid);
                Assert.False(state.Expired);
            },
            async run =>
            {
                await AssertNoDeadLettersAsync(run);

                // neither retried nor refused: no host gave a message back
                Assert.Empty(run.ProcessingFailures);
            });

        // also after the observation window: the timeout was delivered, and nothing handled it or what it would publish
        Assert.True(timeoutDelivered.Count >= 1);
        Assert.Single(runs.RunsOf<PaymentReceived>());
        Assert.Empty(runs.RunsOf<PaymentTimedOut>());
        Assert.Empty(runs.RunsOf<OrderExpired>());
    }
}
