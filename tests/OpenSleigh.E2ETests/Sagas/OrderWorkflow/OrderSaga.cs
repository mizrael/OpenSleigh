using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas.OrderWorkflow;

public class OrderState
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public List<string> Skus { get; set; } = [];
    public bool CreditChecked { get; set; }
    public List<string> ReservedSkus { get; set; } = [];
    public int ShipmentsRequested { get; set; }
    public string? TrackingNumber { get; set; }
}

/// <summary>
/// the orchestrator of the order workflow, shaped like the one of the ECommerce sample: it asks for a credit check
/// and for the stock of every line at the same time, and ships the order once every answer is in, in whatever order.
/// </summary>
public class OrderSaga(ISagaInstance<OrderState> context) :
    Saga<OrderState>(context),
    IStartedBy<PlaceOrder>,
    IHandleMessage<CreditChecked>,
    IHandleMessage<StockReserved>,
    IHandleMessage<OrderShipped>
{
    public ValueTask HandleAsync(IMessageContext<PlaceOrder> context, CancellationToken cancellationToken = default)
    {
        var order = context.Message;
        Context.State.OrderId = order.OrderId;
        Context.State.Amount = order.Amount;
        Context.State.Skus = order.Skus.ToList();

        Publish(new CheckCredit(order.OrderId, order.Amount));
        Publish(new ReserveStock(order.OrderId, order.Skus));

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
        Context.State.ReservedSkus.Add(context.Message.Sku);
        ShipWhenReady();

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<OrderShipped> context, CancellationToken cancellationToken = default)
    {
        Context.State.TrackingNumber = context.Message.TrackingNumber;
        Context.MarkAsCompleted();

        return ValueTask.CompletedTask;
    }

    // the answers are handled one after the other, each on the state the previous one left: only the last one ships.
    // An answer handled on a state that misses another would ship twice, or never
    private void ShipWhenReady()
    {
        if (!Context.State.CreditChecked || Context.State.ReservedSkus.Count != Context.State.Skus.Count)
            return;

        Context.State.ShipmentsRequested++;
        Publish(new ShipOrder(Context.State.OrderId));
    }
}
