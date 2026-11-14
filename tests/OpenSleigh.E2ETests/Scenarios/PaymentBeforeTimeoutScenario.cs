using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the reply a saga was waiting for arrives before its timeout is due: the timeout has nothing left to do.
/// </summary>
public class PaymentBeforeTimeoutScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // long enough for the payment to be handled first on a busy machine: the scenario publishes it
    // as soon as the order is stored
    private static readonly TimeSpan PaymentDueWithin = TimeSpan.FromSeconds(3);

    // in memory, where there is one host, and with a few hosts everywhere else: the timeout is one row in an outbox
    // that every host relays, and whichever host receives it has to leave it alone
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task an_order_paid_in_time_does_not_expire(string combination, int hostsCount)
        => PayAnOrderInTimeAsync(combination, hostsCount);

    [Theory]
    [MatrixData(HostsCounts = [3])]
    public Task an_order_paid_in_time_does_not_expire_with_several_hosts(string combination, int hostsCount)
        => PayAnOrderInTimeAsync(combination, hostsCount);

    private async Task PayAnOrderInTimeAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var journal = new OrderPaymentJournal();

        // the timeout of a completed saga reaches no handler: the hosts tell when they are done with it
        var timeoutDropped = new Expectation(1);

        var setup = new HostSetup(OrderPaymentJournal.AddSagas)
        {
            ConfigureServices = services => journal.AddTo(services).ObserveDeliveriesOf<PaymentTimedOut>(timeoutDropped)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishAsync(new PlaceOrder(correlationId, PaymentDueWithin), cancellationToken);

                // a payment that arrives before its order is stored has no saga to go to
                await run.WaitForSagaAsync<OrderPaymentSaga>(correlationId,
                    stored => stored.ProcessedMessages.Count == 1 && !stored.IsLocked, cancellationToken);

                await run.AnyHost.PublishAsync(new PaymentReceived(correlationId), cancellationToken);
                await journal.Paid.Reached.WaitAsync(cancellationToken);

                // the timeout is still in the outbox, and becomes due: a host receives it and leaves it at that.
                // The in-memory outbox does not count a message that is not due, so its being empty would prove nothing
                await timeoutDropped.Reached.WaitAsync(cancellationToken);

                var order = await run.WaitForSagaAsync<OrderPaymentSaga>(correlationId, stored => stored.IsCompleted, cancellationToken);
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

        Assert.Equal(1, journal.Paid.Count);
        Assert.Equal(1, timeoutDropped.Count);
        Assert.Equal(0, journal.TimedOut.Count);
        Assert.Equal(0, journal.ExpiredOrderHandled.Count);
    }
}
