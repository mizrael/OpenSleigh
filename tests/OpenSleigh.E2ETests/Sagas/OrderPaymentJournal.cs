using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

/// <summary>
/// what the order sagas of a host, or of several, reported, and when.
/// </summary>
public sealed class OrderPaymentJournal
{
    private readonly ConcurrentQueue<DateTimeOffset> _placedAt = new();
    private readonly ConcurrentQueue<DateTimeOffset> _timedOutAt = new();

    public Expectation Placed { get; } = new(1);
    public Expectation Paid { get; } = new(1);
    public Expectation TimedOut { get; } = new(1);
    public Expectation ExpiredOrderHandled { get; } = new(1);

    /// <summary>when each order was placed: read before its timeout was published.</summary>
    public IReadOnlyCollection<DateTimeOffset> PlacedAt => _placedAt;

    /// <summary>when each timeout was handled.</summary>
    public IReadOnlyCollection<DateTimeOffset> TimedOutAt => _timedOutAt;

    public static void AddSagas(IBusConfigurator bus)
        => bus.AddSaga<OrderPaymentSaga, OrderPaymentState>()
              .AddSaga<ExpiredOrderSaga>();

    /// <summary>makes the sagas of a host report to this journal.</summary>
    public IServiceCollection AddTo(IServiceCollection services)
    {
        Action<IMessageContext<PlaceOrder>> onOrderPlaced = _ =>
        {
            _placedAt.Enqueue(DateTimeOffset.UtcNow);
            Placed.Signal();
        };
        Action<IMessageContext<PaymentReceived>> onPaid = _ => Paid.Signal();
        Action<IMessageContext<PaymentTimedOut>> onTimedOut = _ =>
        {
            _timedOutAt.Enqueue(DateTimeOffset.UtcNow);
            TimedOut.Signal();
        };
        Action<IMessageContext<OrderExpired>> onOrderExpired = _ => ExpiredOrderHandled.Signal();

        return services
            .AddSingleton(onOrderPlaced)
            .AddSingleton(onPaid)
            .AddSingleton(onTimedOut)
            .AddSingleton(onOrderExpired);
    }

    public HostSetup CreateHostSetup()
        => new(AddSagas) { ConfigureServices = services => AddTo(services) };

    // MongoDB stores the due time in milliseconds, and drops the rest
    private static readonly TimeSpan DueTimePrecision = TimeSpan.FromMilliseconds(1);

    // how late a timeout is depends on how often the outbox is read and on how busy the machine is:
    // this only tells a timeout that is late from one that waited for something else, such as a lock to expire
    private static readonly TimeSpan LatestAfterDueTime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// the one timeout of the one order was handled once: not before it was due, and not much later.
    /// </summary>
    /// <param name="placed">the journal of the host that handled the order.</param>
    /// <param name="timedOut">the journal of the host that handled the timeout.</param>
    public static void AssertTimedOutOnceWhenDue(OrderPaymentJournal placed, OrderPaymentJournal timedOut, TimeSpan paymentDueWithin)
    {
        var placedAt = Assert.Single(placed.PlacedAt);
        var timedOutAt = Assert.Single(timedOut.TimedOutAt);
        var waited = timedOutAt - placedAt;

        Assert.True(waited >= paymentDueWithin - DueTimePrecision,
            $"the timeout was handled {waited.TotalMilliseconds} ms after the order was placed, before the {paymentDueWithin.TotalMilliseconds} ms it had to wait.");
        Assert.True(waited <= paymentDueWithin + LatestAfterDueTime,
            $"the timeout was handled {waited.TotalMilliseconds} ms after the order was placed: it was due after {paymentDueWithin.TotalMilliseconds} ms.");
    }
}
