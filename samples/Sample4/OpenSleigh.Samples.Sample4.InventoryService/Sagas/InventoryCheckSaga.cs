using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample4.Common;

namespace OpenSleigh.Samples.Sample4.InventoryService.Sagas
{
    public class InventoryCheckSagaState : SagaState{
        public InventoryCheckSagaState(Guid id) : base(id){}
    }
    
    public class InventoryCheckSaga :
        Saga<InventoryCheckSagaState>,
        IStartedBy<CheckInventory>
    {
        private readonly ILogger<InventoryCheckSaga> _logger;

        public InventoryCheckSaga(ILogger<InventoryCheckSaga> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IMessageContext<CheckInventory> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"checking inventory for order '{context.Message.OrderId}'...");
            
            var message = InventoryCheckCompleted.New(context.Message.OrderId);
            await this.Bus.PublishAsync(message, cancellationToken);

            this.State.MarkAsCompleted();
        }
    }
}
