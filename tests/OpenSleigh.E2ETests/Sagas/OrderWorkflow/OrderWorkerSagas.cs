using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas.OrderWorkflow;

public class CreditCheckSaga(ISagaInstance context) : Saga(context), IStartedBy<CheckCredit>
{
    public ValueTask HandleAsync(IMessageContext<CheckCredit> context, CancellationToken cancellationToken = default)
    {
        Publish(new CreditChecked(context.Message.OrderId));
        Context.MarkAsCompleted();

        return ValueTask.CompletedTask;
    }
}

public class StockReservationSaga(ISagaInstance context) : Saga(context), IStartedBy<ReserveStock>
{
    public ValueTask HandleAsync(IMessageContext<ReserveStock> context, CancellationToken cancellationToken = default)
    {
        Publish(new StockReserved(context.Message.OrderId));
        Context.MarkAsCompleted();

        return ValueTask.CompletedTask;
    }
}

public class ShippingSaga(ISagaInstance context) : Saga(context), IStartedBy<ShipOrder>
{
    public static string TrackingNumberOf(Guid orderId) => $"TRK-{orderId:N}";

    public ValueTask HandleAsync(IMessageContext<ShipOrder> context, CancellationToken cancellationToken = default)
    {
        var orderId = context.Message.OrderId;
        Publish(new OrderShipped(orderId, TrackingNumberOf(orderId)));
        Context.MarkAsCompleted();

        return ValueTask.CompletedTask;
    }
}
