using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

/// <param name="PaymentDueWithin">how long the order waits for its payment before it expires.</param>
public record PlaceOrder(string CorrelationId, TimeSpan PaymentDueWithin) : IMessage, IHasCorrelationId;

public record PaymentReceived(string CorrelationId) : IMessage, IHasCorrelationId;

public record PaymentTimedOut : IMessage;

public record OrderExpired : IMessage;

public class OrderPaymentState
{
    public bool Paid { get; set; }
    public bool Expired { get; set; }
}

/// <summary>
/// an order that waits for its payment, and gives up when it does not come in time:
/// the saga publishes its own timeout, with a delay, when the order is placed.
/// </summary>
public class OrderPaymentSaga(ISagaInstance<OrderPaymentState> context) :
    Saga<OrderPaymentState>(context),
    IStartedBy<PlaceOrder>,
    IHandleMessage<PaymentReceived>,
    IHandleMessage<PaymentTimedOut>
{
    public ValueTask HandleAsync(IMessageContext<PlaceOrder> context, CancellationToken cancellationToken = default)
    {
        Publish(new PaymentTimedOut(), context.Message.PaymentDueWithin);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<PaymentReceived> context, CancellationToken cancellationToken = default)
    {
        Context.State.Paid = true;
        Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<PaymentTimedOut> context, CancellationToken cancellationToken = default)
    {
        Context.State.Expired = true;
        Publish(new OrderExpired());
        Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// what the rest of the system does with an order that expired, such as giving its stock back.
/// </summary>
public class ExpiredOrderSaga(ISagaInstance context) :
    Saga(context),
    IStartedBy<OrderExpired>
{
    public ValueTask HandleAsync(IMessageContext<OrderExpired> context, CancellationToken cancellationToken = default)
    {
        Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }
}
