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
public class OrderPaymentSaga :
    Saga<OrderPaymentState>,
    IStartedBy<PlaceOrder>,
    IHandleMessage<PaymentReceived>,
    IHandleMessage<PaymentTimedOut>
{
    private readonly Action<IMessageContext<PlaceOrder>> _onOrderPlaced;
    private readonly Action<IMessageContext<PaymentReceived>> _onPaid;
    private readonly Action<IMessageContext<PaymentTimedOut>> _onTimedOut;

    public OrderPaymentSaga(
        Action<IMessageContext<PlaceOrder>> onOrderPlaced,
        Action<IMessageContext<PaymentReceived>> onPaid,
        Action<IMessageContext<PaymentTimedOut>> onTimedOut,
        ISagaInstance<OrderPaymentState> context) : base(context)
    {
        _onOrderPlaced = onOrderPlaced ?? throw new ArgumentNullException(nameof(onOrderPlaced));
        _onPaid = onPaid ?? throw new ArgumentNullException(nameof(onPaid));
        _onTimedOut = onTimedOut ?? throw new ArgumentNullException(nameof(onTimedOut));
    }

    public ValueTask HandleAsync(IMessageContext<PlaceOrder> context, CancellationToken cancellationToken = default)
    {
        // reported before the timeout is published: what is read then is not later than the time the delay starts from
        _onOrderPlaced(context);

        Publish(new PaymentTimedOut(), context.Message.PaymentDueWithin);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<PaymentReceived> context, CancellationToken cancellationToken = default)
    {
        Context.State.Paid = true;
        Context.MarkAsCompleted();

        _onPaid(context);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<PaymentTimedOut> context, CancellationToken cancellationToken = default)
    {
        Context.State.Expired = true;
        Publish(new OrderExpired());
        Context.MarkAsCompleted();

        _onTimedOut(context);

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// what the rest of the system does with an order that expired, such as giving its stock back.
/// </summary>
public class ExpiredOrderSaga :
    Saga,
    IStartedBy<OrderExpired>
{
    private readonly Action<IMessageContext<OrderExpired>> _onOrderExpired;

    public ExpiredOrderSaga(Action<IMessageContext<OrderExpired>> onOrderExpired, ISagaInstance context) : base(context)
    {
        _onOrderExpired = onOrderExpired ?? throw new ArgumentNullException(nameof(onOrderExpired));
    }

    public ValueTask HandleAsync(IMessageContext<OrderExpired> context, CancellationToken cancellationToken = default)
    {
        Context.MarkAsCompleted();

        _onOrderExpired(context);

        return ValueTask.CompletedTask;
    }
}
