using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample2.Common.Messages;

namespace OpenSleigh.Samples.Sample2.Worker.Sagas
{
    public class ParentSagaState : SagaState{
        public ParentSagaState(Guid id) : base(id){}
    }

    public record ProcessParentSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ParentSagaCompleted(Guid Id, Guid CorrelationId) : IEvent { }

    public class ParentSaga :
        Saga<ParentSagaState>,
        IStartedBy<StartParentSaga>,
        IHandleMessage<ProcessParentSaga>,
        IHandleMessage<ChildSagaCompleted>,
        IHandleMessage<ParentSagaCompleted>
    {
        private readonly ILogger<ParentSaga> _logger;

        private readonly Random _random = new Random();

        public ParentSaga(ILogger<ParentSaga> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IMessageContext<StartParentSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting parent saga '{context.Message.CorrelationId}'...");
            
            var message = new ProcessParentSaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }
        
        public async Task HandleAsync(IMessageContext<ProcessParentSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting child saga from parent saga '{context.Message.CorrelationId}'...");
            
            var message = new StartChildSaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public async Task HandleAsync(IMessageContext<ChildSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"child saga completed, finalizing parent saga '{context.Message.CorrelationId}'...");

            await Task.Delay(TimeSpan.FromSeconds(_random.Next(1, 5)), cancellationToken);
            
            var message = new ParentSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public Task HandleAsync(IMessageContext<ParentSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted(); 
            _logger.LogInformation($"parent saga '{context.Message.CorrelationId}' completed!");
            return Task.CompletedTask;
        }
    }
}
