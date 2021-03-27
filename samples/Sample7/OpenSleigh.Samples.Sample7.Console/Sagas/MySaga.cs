using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample7.Console.Sagas
{
    public class MySagaState : SagaState{
        public MySagaState(Guid id) : base(id){}

        public enum Steps
        {
            Processing,
            Successful,
            Failed
        };
        public Steps CurrentStep { get; set; } = Steps.Processing;
    }

    public record StartSaga(Guid Id, Guid CorrelationId, bool WillFail = false) : ICommand { }
        
    public record MySagaCompleted(Guid Id, Guid CorrelationId) : IEvent { }

    public class MySaga :
        Saga<MySagaState>,
        IStartedBy<StartSaga>,
        ICompensateMessage<StartSaga>,
        IHandleMessage<MySagaCompleted>
    {
        private readonly ILogger<MySaga> _logger;
        
        public MySaga(ILogger<MySaga> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IMessageContext<StartSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting saga '{context.Message.CorrelationId}'...");

            if (context.Message.WillFail)
                throw new ApplicationException("something, somewhere, went terribly, terribly wrong.");

            this.State.CurrentStep = MySagaState.Steps.Successful;
            
            var message = new MySagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public async Task CompensateAsync(ICompensationContext<StartSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogWarning($"saga '{context.MessageContext.Message.CorrelationId}' failed! Reason: {context.Exception.Message}");
            
            this.State.CurrentStep = MySagaState.Steps.Failed;

            var message = new MySagaCompleted(Guid.NewGuid(), context.MessageContext.Message.CorrelationId);
            this.Publish(message);
        }

        public Task HandleAsync(IMessageContext<MySagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted();

            var isFailed = this.State.CurrentStep == MySagaState.Steps.Failed;
            if(isFailed)
                _logger.LogWarning($"saga '{context.Message.CorrelationId}' failed!");
            else 
                _logger.LogInformation($"saga '{context.Message.CorrelationId}' completed!");
                
            return Task.CompletedTask;
        }
    }
}
