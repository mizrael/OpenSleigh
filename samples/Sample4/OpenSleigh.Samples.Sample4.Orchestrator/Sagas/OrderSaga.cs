using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample4.Common;

namespace OpenSleigh.Samples.Sample4.Orchestrator.Sagas
{
    public class OrderSagaState : SagaState{
        public OrderSagaState(Guid id) : base(id){}

        public Guid OrderId { get; set; }
        public bool CreditCheckCompleted { get; set; } = false;
    }

    public class OrderSaga :
        Saga<OrderSagaState>,
        IStartedBy<SaveOrder>,
        IHandleMessage<CrediCheckCompleted>,
        IHandleMessage<OrderSagaCompleted>
    {
        private readonly ILogger<OrderSaga> _logger;

        public OrderSaga(ILogger<OrderSaga> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IMessageContext<SaveOrder> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"processing order '{context.Message.OrderId}'...");

            this.State.OrderId = context.Message.OrderId;

            var message = ProcessCreditCheck.New(context.Message.OrderId);
            await this.Bus.PublishAsync(message, cancellationToken);
        }
        
        public async Task HandleAsync(IMessageContext<CrediCheckCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"credit check for order '{context.Message.OrderId}' completed!");

            this.State.CreditCheckCompleted = true;

            await CheckStateAsync(cancellationToken);
        }

        public Task HandleAsync(IMessageContext<OrderSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted(); 
            _logger.LogInformation($"Order saga '{context.Message.CorrelationId}' completed!");
            return Task.CompletedTask;
        }
        
        private async Task CheckStateAsync(CancellationToken cancellationToken = default)
        {
            var checksFulfilled = this.State.CreditCheckCompleted;
            if (!checksFulfilled)
                return;

            var message = OrderSagaCompleted.New(this.State.OrderId);
            await this.Bus.PublishAsync(message, cancellationToken);
        }

    }
}
