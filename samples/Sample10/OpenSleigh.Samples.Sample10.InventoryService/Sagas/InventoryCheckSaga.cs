using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample10.Common;

namespace OpenSleigh.Samples.Sample10.InventoryService.Sagas
{
    public record InventoryCheckSagaState : SagaState{
        public InventoryCheckSagaState(Guid id) : base(id){}
    }
    
    public class InventoryCheckSaga :
        Saga<InventoryCheckSagaState>,
        IStartedBy<CheckInventory>
    {
        private readonly ILogger<InventoryCheckSaga> _logger;

        public InventoryCheckSaga(ILogger<InventoryCheckSaga> logger, InventoryCheckSagaState state) : base(state)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IMessageContext<CheckInventory> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"checking inventory for order '{context.Message.OrderId}'...");
            
            var message = InventoryCheckCompleted.New(context.Message.OrderId);
            this.Publish(message);

            _logger.LogInformation($"inventory check for order '{context.Message.OrderId}' completed!");

            this.State.MarkAsCompleted();
        }
    }
}
