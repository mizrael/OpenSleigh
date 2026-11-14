using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a saga waits for a reply that never comes: the timeout it published for itself, with a delay, is what moves it on.
/// </summary>
public class PaymentTimeoutScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private static readonly TimeSpan PaymentDueWithin = TimeSpan.FromSeconds(2);

    // one host, and a few: every host relays the outbox, and the timeout is one row in it
    [Theory]
    [MatrixData(HostsCounts = [1, 3])]
    public async Task an_order_that_is_not_paid_in_time_expires_once(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var journal = new OrderPaymentJournal();

        await RunScenarioAsync(combination, hostsCount, journal.CreateHostSetup(), async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishAsync(new PlaceOrder(correlationId, PaymentDueWithin), cancellationToken);

            await journal.TimedOut.Reached.WaitAsync(cancellationToken);
            await journal.ExpiredOrderHandled.Reached.WaitAsync(cancellationToken);

            var order = await run.WaitForSagaAsync<OrderPaymentSaga>(correlationId, stored => stored.IsCompleted, cancellationToken);
            var state = Assert.IsType<OrderPaymentState>(order.StateData);
            Assert.True(state.Expired);
            Assert.False(state.Paid);
        });

        Assert.Equal(1, journal.Placed.Count);
        Assert.Equal(1, journal.TimedOut.Count);
        Assert.Equal(1, journal.ExpiredOrderHandled.Count);
        Assert.Equal(0, journal.Paid.Count);
        OrderPaymentJournal.AssertTimedOutOnceWhenDue(journal, journal, PaymentDueWithin);
    }
}
