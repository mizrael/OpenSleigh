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
public class ShipmentSaga :
    Saga<ShipmentState>,
    IStartedBy<StartShipment>,
    IHandleMessage<PackParcel>,
    IHandleMessage<ParcelPacked>
{
    private readonly Action<IMessageContext<PackParcel>> _onPacking;
    private readonly Action<IMessageContext<ParcelPacked>> _onPacked;

    public ShipmentSaga(
        Action<IMessageContext<PackParcel>> onPacking,
        Action<IMessageContext<ParcelPacked>> onPacked,
        ISagaInstance<ShipmentState> context) : base(context)
    {
        _onPacking = onPacking ?? throw new ArgumentNullException(nameof(onPacking));
        _onPacked = onPacked ?? throw new ArgumentNullException(nameof(onPacked));
    }

    public ValueTask HandleAsync(IMessageContext<StartShipment> context, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask HandleAsync(IMessageContext<PackParcel> context, CancellationToken cancellationToken = default)
    {
        Context.State.PackingAttempts++;
        Publish(new ParcelPacked());

        _onPacking(context);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<ParcelPacked> context, CancellationToken cancellationToken = default)
    {
        Context.MarkAsCompleted();

        _onPacked(context);

        return ValueTask.CompletedTask;
    }
}
