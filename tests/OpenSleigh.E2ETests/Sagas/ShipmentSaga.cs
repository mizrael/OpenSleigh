using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests;

public record StartShipment(string CorrelationId) : IMessage, IHasCorrelationId;

/// <summary>the message whose handler always fails.</summary>
public record LoadShipment(string CorrelationId, bool CompensationFails = false) : IMessage, IHasCorrelationId;

/// <summary>published by the handler that fails: it must never be delivered.</summary>
public record ShipmentLoaded : IMessage;

/// <summary>published by the compensation of <see cref="LoadShipment"/>.</summary>
public record ShipmentCancelled : IMessage;

public class ShipmentState
{
    public List<string> Steps { get; set; } = [];
}

/// <summary>
/// what the shipment sagas of a scenario report, from whatever host they run on.
/// </summary>
public sealed class ShipmentProbe
{
    private int _loadAttempts;
    private int _loadedDeliveries;
    private int _compensations;

    /// <summary>how many times the handler that fails ran.</summary>
    public int LoadAttempts => Volatile.Read(ref _loadAttempts);

    /// <summary>how many times the message published by the handler that fails was delivered.</summary>
    public int LoadedDeliveries => Volatile.Read(ref _loadedDeliveries);

    public int Compensations => Volatile.Read(ref _compensations);

    /// <summary>the cancellation published by the compensation was handled.</summary>
    public Expectation Cancelled { get; } = new(1);

    public void LoadAttempted() => Interlocked.Increment(ref _loadAttempts);
    public void LoadedDelivered() => Interlocked.Increment(ref _loadedDeliveries);
    public void Compensated() => Interlocked.Increment(ref _compensations);
}

/// <summary>
/// a saga whose handler for <see cref="LoadShipment"/> changes the state, completes the saga,
/// publishes a message and then fails. It has no compensation handler.
/// </summary>
public class ShipmentSaga :
    Saga<ShipmentState>,
    IStartedBy<StartShipment>,
    IHandleMessage<LoadShipment>,
    IHandleMessage<ShipmentLoaded>,
    IHandleMessage<ShipmentCancelled>
{
    public const string Started = "started";
    public const string Loaded = "loaded";
    public const string Cancelled = "cancelled";
    public const string LoadFailure = "the truck is full";

    protected ShipmentProbe Probe { get; }

    public ShipmentSaga(ShipmentProbe probe, ISagaInstance<ShipmentState> context) : base(context)
    {
        Probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public ValueTask HandleAsync(IMessageContext<StartShipment> messageContext, CancellationToken cancellationToken = default)
    {
        Context.State.Steps.Add(Started);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<LoadShipment> messageContext, CancellationToken cancellationToken = default)
    {
        Probe.LoadAttempted();

        Context.State.Steps.Add(Loaded);
        Context.MarkAsCompleted();
        Publish(new ShipmentLoaded());

        throw new InvalidOperationException(LoadFailure);
    }

    public ValueTask HandleAsync(IMessageContext<ShipmentLoaded> messageContext, CancellationToken cancellationToken = default)
    {
        Probe.LoadedDelivered();
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<ShipmentCancelled> messageContext, CancellationToken cancellationToken = default)
    {
        Probe.Cancelled.Require(
            Context.State.Steps.SequenceEqual([Started, Cancelled]),
            $"the cancellation found the steps '{string.Join(",", Context.State.Steps)}'");
        Probe.Cancelled.Signal();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// the same saga, with a compensation handler for the message whose handler fails.
/// </summary>
public class CompensatingShipmentSaga : ShipmentSaga, ICompensateMessage<LoadShipment>
{
    public const string CompensationFailure = "the warehouse did not answer";

    public CompensatingShipmentSaga(ShipmentProbe probe, ISagaInstance<ShipmentState> context) : base(probe, context)
    {
    }

    public ValueTask CompensateAsync(IMessageContext<LoadShipment> messageContext, MessageFailure failure, CancellationToken cancellationToken = default)
    {
        Probe.Compensated();
        Probe.Cancelled.Require(
            Context.State.Steps.SequenceEqual([Started]),
            $"the compensation found the steps '{string.Join(",", Context.State.Steps)}'");
        Probe.Cancelled.Require(!Context.IsCompleted, "the compensation found the saga completed");
        Probe.Cancelled.Require(
            failure.Exception.Message == LoadFailure,
            $"the compensation was told '{failure.Exception.Message}'");

        Context.State.Steps.Add(Cancelled);
        Publish(new ShipmentCancelled());

        if (messageContext.Message.CompensationFails)
            throw new TimeoutException(CompensationFailure);

        return ValueTask.CompletedTask;
    }
}
