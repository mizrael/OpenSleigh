using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Samples.ECommerce.Common;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.ECommerce.InventoryService.Sagas;

public record InventoryCheckSagaState;

public class InventoryCheckSaga :
    Saga<InventoryCheckSagaState>,
    IStartedBy<CheckInventory>
{
    private readonly ILogger<InventoryCheckSaga> _logger;

    public InventoryCheckSaga(
        ILogger<InventoryCheckSaga> logger, 
        ISagaExecutionContext<InventoryCheckSagaState> context) : base(context)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async ValueTask HandleAsync(IMessageContext<CheckInventory> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"checking inventory for order '{context.Message.OrderId}'...");
        
        var message = new InventoryCheckCompleted(context.Message.OrderId);
        this.Publish(message);

        _logger.LogInformation($"inventory check for order '{context.Message.OrderId}' completed!");

        this.Context.MarkAsCompleted();
    }
}
