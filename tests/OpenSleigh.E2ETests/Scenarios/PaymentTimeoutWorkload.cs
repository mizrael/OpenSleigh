using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// what the scenarios about the timeout of an order share: their hosts, and when a timeout may be handled.
/// </summary>
internal static class PaymentTimeoutWorkload
{
    // the due time is set and compared with the clock of the machine, and stored in milliseconds by MongoDB;
    // the handler runs are timed with a stopwatch. A timeout that did not wait at all is early by the whole delay
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMilliseconds(50);

    // how late a timeout is depends on how often the outbox is read and on how busy the machine is:
    // this only tells a timeout that is late from one that waited for something else, such as a claim to expire
    private static readonly TimeSpan LatestAfterDueTime = TimeSpan.FromSeconds(30);

    public static HostSetup HostSetup(Action<IServiceCollection> configureServices)
        => new(bus => bus.AddSaga<OrderPaymentSaga, OrderPaymentState>().AddSaga<ExpiredOrderSaga>())
        {
            ConfigureServices = configureServices
        };

    /// <summary>
    /// the timeout was handled not before it was due, and not much later.
    /// </summary>
    /// <param name="placed">the run of the handler that published the timeout: it started before the delay did.</param>
    public static void AssertHandledWhenDue(HandlerRun placed, HandlerRun timedOut, TimeSpan paymentDueWithin)
    {
        var waited = Stopwatch.GetElapsedTime(placed.At, timedOut.At);

        Assert.True(waited >= paymentDueWithin - ClockTolerance,
            $"the timeout was handled {waited.TotalMilliseconds} ms after the order was placed, before the {paymentDueWithin.TotalMilliseconds} ms it had to wait.");
        Assert.True(waited <= paymentDueWithin + LatestAfterDueTime,
            $"the timeout was handled {waited.TotalMilliseconds} ms after the order was placed: it was due after {paymentDueWithin.TotalMilliseconds} ms.");
    }
}
