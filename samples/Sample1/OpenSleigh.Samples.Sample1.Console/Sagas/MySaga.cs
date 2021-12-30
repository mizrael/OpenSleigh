using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample1.Console.Sagas
{
    public record MySagaState : SagaState{
        public MySagaState(Guid id) : base(id){}
    }

    public record StartSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ProcessMySaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record MySagaCompleted(Guid Id, Guid CorrelationId) : IEvent { }

    public class MySaga :
        Saga<MySagaState>,
        IStartedBy<StartSaga>,
        IHandleMessage<ProcessMySaga>,
        IHandleMessage<MySagaCompleted>
    {
        private readonly ILogger<MySaga> _logger;

        public MySaga(ILogger<MySaga> logger, MySagaState state): base(state)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public Task HandleAsync(IMessageContext<StartSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting saga '{context.Message.CorrelationId}'...");
            
            var message = new ProcessMySaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);

            return Task.CompletedTask;
        }
        
        public Task HandleAsync(IMessageContext<ProcessMySaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"processing saga '{context.Message.CorrelationId}'...");
            
            var message = new MySagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);

            return Task.CompletedTask;
        }

        public Task HandleAsync(IMessageContext<MySagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted(); 

            _logger.LogInformation($"saga '{context.Message.CorrelationId}' completed!");

            return Task.CompletedTask;
        }
    }
}
