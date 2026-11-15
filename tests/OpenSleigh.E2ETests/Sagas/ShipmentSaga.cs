using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record StartShipment(string CorrelationId) : IMessage, IHasCorrelationId;

public record PackParcel(string CorrelationId) : IMessage, IHasCorrelationId;

public record ParcelPacked : IMessage;

public class ShipmentState
{
    public int PackingAttempts { get; set; }
}

/// <summary>
/// a shipment that is asked to pack its parcel, changes its state and tells the others when it has.
/// </summary>
public class ShipmentSaga(ISagaInstance<ShipmentState> context) :
    Saga<ShipmentState>(context),
    IStartedBy<StartShipment>,
    IHandleMessage<PackParcel>,
    IHandleMessage<ParcelPacked>
{
    public ValueTask HandleAsync(IMessageContext<StartShipment> context, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask HandleAsync(IMessageContext<PackParcel> context, CancellationToken cancellationToken = default)
    {
        Context.State.PackingAttempts++;
        Publish(new ParcelPacked());
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<ParcelPacked> context, CancellationToken cancellationToken = default)
    {
        Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }
}
