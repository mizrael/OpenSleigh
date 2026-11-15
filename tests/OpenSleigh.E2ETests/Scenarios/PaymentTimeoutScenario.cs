using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a saga waits for a reply that never comes: the timeout it published for itself, with a delay, is what moves it on.
/// </summary>
public class PaymentTimeoutScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int MessagesOfAnOrderThatExpires = 2;
    private static readonly TimeSpan PaymentDueWithin = TimeSpan.FromSeconds(2);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task an_order_that_is_not_paid_in_time_expires_once(string combination, int hostsCount)
        => LetAnOrderExpireAsync(combination, hostsCount);

    // every host relays the outbox, and the timeout is one row in it: several hosts, one timeout.
    // One host on these combinations is PaymentTimeoutAfterRestartScenario. A delay is kept by the outbox of the store,
    // and a transport is given the timeout like any message: every store runs it, and one combination has Kafka
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [3])]
    public Task an_order_that_is_not_paid_in_time_expires_once_with_several_hosts(string combination, int hostsCount)
        => LetAnOrderExpireAsync(combination, hostsCount);

    private async Task LetAnOrderExpireAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns();
        var setup = PaymentTimeoutWorkload.HostSetup(services => services.RecordHandlerRuns(runs));

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishNewAsync(new PlaceOrder(correlationId, PaymentDueWithin), cancellationToken);

            var order = await run.WaitForReleasedSagaAsync<OrderPaymentSaga>(correlationId, MessagesOfAnOrderThatExpires, cancellationToken);
            Assert.True(order.IsCompleted);

            var state = Assert.IsType<OrderPaymentState>(order.StateData);
            Assert.True(state.Expired);
            Assert.False(state.Paid);

            await run.WaitUntilAsync("the message the timeout published to be handled",
                () => runs.RunsOf<OrderExpired>().Count >= 1, cancellationToken);
        });

        // also after the observation window: no host handled the timeout, or what it published, a second time
        var placed = Assert.Single(runs.RunsOf<PlaceOrder>());
        var timedOut = Assert.Single(runs.RunsOf<PaymentTimedOut>());
        Assert.Single(runs.RunsOf<OrderExpired>());
        Assert.Empty(runs.RunsOf<PaymentReceived>());
        PaymentTimeoutWorkload.AssertHandledWhenDue(placed, timedOut, PaymentDueWithin);
    }
}
