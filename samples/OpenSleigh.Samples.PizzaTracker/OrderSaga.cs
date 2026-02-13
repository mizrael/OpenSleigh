using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.PizzaTracker;

public class OrderSaga :
    Saga<OrderState>,
    IStartedBy<PlaceOrder>,
    IHandleMessage<PreparePizza>,
    IHandleMessage<BakePizza>,
    IHandleMessage<QualityCheck>,
    IHandleMessage<SendOutForDelivery>,
    IHandleMessage<ConfirmDelivery>
{
    private readonly ILogger<OrderSaga> _logger;

    public OrderSaga(ILogger<OrderSaga> logger, ISagaInstance<OrderState> context) : base(context)
    {
        _logger = logger;
    }

    public async ValueTask HandleAsync(IMessageContext<PlaceOrder> context, CancellationToken cancellationToken = default)
    {
        var msg = context.Message;
        this.Context.State.CustomerName = msg.CustomerName;
        this.Context.State.PizzaType = msg.PizzaType;
        this.Context.State.OrderedAt = DateTime.UtcNow;
        this.Context.State.Status = "Received";

        _logger.LogInformation("🍕 Order received: {PizzaType} for {Customer}", msg.PizzaType, msg.CustomerName);

        await SimulateWorkAsync(cancellationToken);
        this.Publish(new PreparePizza());
    }

    public async ValueTask HandleAsync(IMessageContext<PreparePizza> context, CancellationToken cancellationToken = default)
    {
        this.Context.State.Status = "Preparing";
        _logger.LogInformation("👨‍🍳 Preparing {PizzaType}...", this.Context.State.PizzaType);

        await SimulateWorkAsync(cancellationToken);
        this.Context.State.PreparedAt = DateTime.UtcNow;
        this.Publish(new BakePizza());
    }

    public async ValueTask HandleAsync(IMessageContext<BakePizza> context, CancellationToken cancellationToken = default)
    {
        this.Context.State.Status = "Baking";
        _logger.LogInformation("🔥 Baking {PizzaType} in the oven...", this.Context.State.PizzaType);

        await SimulateWorkAsync(cancellationToken);
        this.Context.State.BakedAt = DateTime.UtcNow;
        this.Publish(new QualityCheck());
    }

    public async ValueTask HandleAsync(IMessageContext<QualityCheck> context, CancellationToken cancellationToken = default)
    {
        this.Context.State.Status = "Quality Check";
        _logger.LogInformation("✅ Quality check on {PizzaType}...", this.Context.State.PizzaType);

        await SimulateWorkAsync(cancellationToken);
        this.Context.State.QualityCheckedAt = DateTime.UtcNow;
        this.Publish(new SendOutForDelivery());
    }

    public async ValueTask HandleAsync(IMessageContext<SendOutForDelivery> context, CancellationToken cancellationToken = default)
    {
        this.Context.State.Status = "Out for Delivery";
        _logger.LogInformation("🚗 {PizzaType} is out for delivery to {Customer}!", this.Context.State.PizzaType, this.Context.State.CustomerName);

        await SimulateWorkAsync(cancellationToken);
        this.Context.State.OutForDeliveryAt = DateTime.UtcNow;
        this.Publish(new ConfirmDelivery());
    }

    public ValueTask HandleAsync(IMessageContext<ConfirmDelivery> context, CancellationToken cancellationToken = default)
    {
        this.Context.State.Status = "Delivered";
        this.Context.State.DeliveredAt = DateTime.UtcNow;

        _logger.LogInformation("🎉 {PizzaType} delivered to {Customer}!", this.Context.State.PizzaType, this.Context.State.CustomerName);
        this.Context.MarkAsCompleted();

        return ValueTask.CompletedTask;
    }

    private static async Task SimulateWorkAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }
}
