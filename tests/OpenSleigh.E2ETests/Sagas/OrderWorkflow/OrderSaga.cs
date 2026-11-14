using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas.OrderWorkflow;

public record OrderState
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public int Quantity { get; set; }
    public bool CreditChecked { get; set; }
    public bool StockReserved { get; set; }
    public int ShipmentsRequested { get; set; }
    public string? TrackingNumber { get; set; }
}

/// <summary>
/// the orchestrator of the order workflow, shaped like the one of the ECommerce sample: it asks for a credit check
/// and a stock reservation at the same time, and ships the order once both have answered, in whatever order.
/// </summary>
public class OrderSaga :
    Saga<OrderState>,
    IStartedBy<PlaceOrder>,
    IHandleMessage<CreditChecked>,
    IHandleMessage<StockReserved>,
    IHandleMessage<OrderShipped>
{
    private readonly Action<IMessageContext<OrderShipped>> _onCompleted;

    public OrderSaga(Action<IMessageContext<OrderShipped>> onCompleted, ISagaInstance<OrderState> context) : base(context)
    {
        _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
    }

    public ValueTask HandleAsync(IMessageContext<PlaceOrder> context, CancellationToken cancellationToken = default)
    {
        var order = context.Message;
        Context.State.OrderId = order.OrderId;
        Context.State.Amount = order.Amount;
        Context.State.Quantity = order.Quantity;

        Publish(new CheckCredit(order.OrderId, order.Amount));
        Publish(new ReserveStock(order.OrderId, order.Quantity));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<CreditChecked> context, CancellationToken cancellationToken = default)
    {
        Context.State.CreditChecked = true;
        ShipWhenReady();

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<StockReserved> context, CancellationToken cancellationToken = default)
    {
        Context.State.StockReserved = true;
        ShipWhenReady();

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<OrderShipped> context, CancellationToken cancellationToken = default)
    {
        Context.State.TrackingNumber = context.Message.TrackingNumber;
        Context.MarkAsCompleted();

        _onCompleted(context);

        return ValueTask.CompletedTask;
    }

    // the two answers are handled one after the other, each on the state the other left: the second one ships
    private void ShipWhenReady()
    {
        if (!Context.State.CreditChecked || !Context.State.StockReserved)
            return;

        Context.State.ShipmentsRequested++;
        Publish(new ShipOrder(Context.State.OrderId));
    }
}
