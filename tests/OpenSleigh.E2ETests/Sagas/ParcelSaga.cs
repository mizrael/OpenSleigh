using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record RegisterParcel(string CorrelationId) : IMessage, IHasCorrelationId;

public record ParcelScanned(string CorrelationId, int Sequence) : IMessage, IHasCorrelationId;

public record ParcelState
{
    /// <summary>the sequence numbers of the scans, in the order they were handled.</summary>
    public List<int> Scans { get; set; } = [];
}

/// <summary>
/// a parcel that is scanned along its way: several messages of one type for one saga instance,
/// sent in a known order. The state keeps the order they were handled in.
/// </summary>
public class ParcelSaga :
    Saga<ParcelState>,
    IStartedBy<RegisterParcel>,
    IHandleMessage<ParcelScanned>
{
    private readonly Action<IMessageContext<ParcelScanned>> _onScanned;

    public ParcelSaga(Action<IMessageContext<ParcelScanned>> onScanned, ISagaInstance<ParcelState> context) : base(context)
    {
        _onScanned = onScanned ?? throw new ArgumentNullException(nameof(onScanned));
    }

    public ValueTask HandleAsync(IMessageContext<RegisterParcel> context, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask HandleAsync(IMessageContext<ParcelScanned> context, CancellationToken cancellationToken = default)
    {
        Context.State.Scans.Add(context.Message.Sequence);
        _onScanned(context);

        return ValueTask.CompletedTask;
    }
}
