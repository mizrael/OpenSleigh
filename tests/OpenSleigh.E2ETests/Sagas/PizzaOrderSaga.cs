using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record PlacePizzaOrder(string CorrelationId, string Customer, string Pizza) : IMessage, IHasCorrelationId;

public record PizzaPrepared(string CorrelationId) : IMessage, IHasCorrelationId;

public record PizzaBaked(string CorrelationId) : IMessage, IHasCorrelationId;

public record PizzaChecked(string CorrelationId) : IMessage, IHasCorrelationId;

public record PizzaOutForDelivery(string CorrelationId) : IMessage, IHasCorrelationId;

public record PizzaDelivered(string CorrelationId) : IMessage, IHasCorrelationId;

public record PizzaOrderState
{
    public string Customer { get; set; } = string.Empty;
    public string Pizza { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>every status the order went through, in order.</summary>
    public List<string> History { get; set; } = [];
}

/// <summary>
/// the order of the PizzaTracker sample, with each stage reported from outside (the kitchen, the driver)
/// instead of by the saga itself: a saga that lives for a while and is woken up by one event after the other.
/// </summary>
public class PizzaOrderSaga(ISagaInstance<PizzaOrderState> context) :
    Saga<PizzaOrderState>(context),
    IStartedBy<PlacePizzaOrder>,
    IHandleMessage<PizzaPrepared>,
    IHandleMessage<PizzaBaked>,
    IHandleMessage<PizzaChecked>,
    IHandleMessage<PizzaOutForDelivery>,
    IHandleMessage<PizzaDelivered>
{
    public const string Received = "Received";
    public const string Prepared = "Prepared";
    public const string Baked = "Baked";
    public const string Checked = "Checked";
    public const string OutForDelivery = "Out for delivery";
    public const string Delivered = "Delivered";

    public ValueTask HandleAsync(IMessageContext<PlacePizzaOrder> context, CancellationToken cancellationToken = default)
    {
        Context.State.Customer = context.Message.Customer;
        Context.State.Pizza = context.Message.Pizza;

        return MoveTo(Received);
    }

    public ValueTask HandleAsync(IMessageContext<PizzaPrepared> context, CancellationToken cancellationToken = default)
        => MoveTo(Prepared);

    public ValueTask HandleAsync(IMessageContext<PizzaBaked> context, CancellationToken cancellationToken = default)
        => MoveTo(Baked);

    public ValueTask HandleAsync(IMessageContext<PizzaChecked> context, CancellationToken cancellationToken = default)
        => MoveTo(Checked);

    public ValueTask HandleAsync(IMessageContext<PizzaOutForDelivery> context, CancellationToken cancellationToken = default)
        => MoveTo(OutForDelivery);

    public ValueTask HandleAsync(IMessageContext<PizzaDelivered> context, CancellationToken cancellationToken = default)
    {
        Context.MarkAsCompleted();

        return MoveTo(Delivered);
    }

    private ValueTask MoveTo(string status)
    {
        Context.State.Status = status;
        Context.State.History.Add(status);

        return ValueTask.CompletedTask;
    }
}
