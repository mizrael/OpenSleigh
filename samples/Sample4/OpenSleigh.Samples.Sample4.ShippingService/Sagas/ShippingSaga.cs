using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample4.Common;

namespace OpenSleigh.Samples.Sample4.ShippingService.Sagas
{
    public class ShippingSagaState : SagaState{
        public ShippingSagaState(Guid id) : base(id){}
    }
    
    public class ShippingSaga :
        Saga<ShippingSagaState>,
        IStartedBy<ProcessShipping>
    {
        private readonly ILogger<ShippingSaga> _logger;

        public ShippingSaga(ILogger<ShippingSaga> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IMessageContext<ProcessShipping> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"processing shipping for order '{context.Message.OrderId}'...");
            
            var message = ShippingCompleted.New(context.Message.OrderId);
            this.Publish(message);

            this.State.MarkAsCompleted();
        }
    }
}
