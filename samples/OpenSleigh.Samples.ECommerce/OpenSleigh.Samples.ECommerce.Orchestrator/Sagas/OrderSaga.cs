using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Samples.ECommerce.Common;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.ECommerce.Orchestrator.Sagas;

public record OrderSagaState
{
    public Guid OrderId { get; set; }
    public bool CreditCheckCompleted { get; set; } = false;
    public bool InventoryCheckCompleted{ get; set; } = false;
}

public class OrderSaga :
    Saga<OrderSagaState>,
    IStartedBy<SaveOrder>,
    IHandleMessage<CrediCheckCompleted>,
    IHandleMessage<InventoryCheckCompleted>,
    IHandleMessage<ShippingCompleted>
{
    private readonly ILogger<OrderSaga> _logger;

    public OrderSaga(
        ISagaExecutionContext<OrderSagaState> context,
        ILogger<OrderSaga> logger) : base(context)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async ValueTask HandleAsync(IMessageContext<SaveOrder> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"processing order '{context.Message.OrderId}'...");

        this.Context.State.OrderId = context.Message.OrderId;

        var startCreditCheck = new ProcessCreditCheck(context.Message.OrderId);
        this.Publish(startCreditCheck);

        var startInventoryCheck = new CheckInventory(context.Message.OrderId);
        this.Publish(startInventoryCheck);
    }
    
    public async ValueTask HandleAsync(IMessageContext<CrediCheckCompleted> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"credit check for order '{context.Message.OrderId}' completed!");

        this.Context.State.CreditCheckCompleted = true;

        if (CheckCanShipOrder(cancellationToken))
        {
            var message = new ProcessShipping(this.Context.State.OrderId);
            this.Publish(message);
        }
    }

    public async ValueTask HandleAsync(IMessageContext<InventoryCheckCompleted> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"inventory check for order '{context.Message.OrderId}' completed!");

        this.Context.State.InventoryCheckCompleted = true;

        if (CheckCanShipOrder(cancellationToken))
        {
            var message = new ProcessShipping(this.Context.State.OrderId);
            this.Publish(message);
        }
    }

    public async ValueTask HandleAsync(IMessageContext<ShippingCompleted> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Processing completed for order '{context.Message.OrderId}'");

        var message = new OrderSagaCompleted(this.Context.State.OrderId);
        this.Publish(message);

        this.Context.MarkAsCompleted();
    }
    
    private bool CheckCanShipOrder(CancellationToken cancellationToken = default)
    {
        var checksFulfilled = this.Context.State.CreditCheckCompleted &&
                              this.Context.State.InventoryCheckCompleted;
        return checksFulfilled;
    }

}
