using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample2.Common.Sagas
{
    public class SimpleSagaState : SagaState{
        public SimpleSagaState(Guid id) : base(id){}
    }

    public record StartSimpleSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ProcessSimpleSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record SimpleSagaCompleted(Guid Id, Guid CorrelationId) : IEvent { }

    public class SimpleSaga :
        Saga<SimpleSagaState>,
        IStartedBy<StartSimpleSaga>,
        IHandleMessage<ProcessSimpleSaga>,
        IHandleMessage<SimpleSagaCompleted>
    {
        private readonly ILogger<SimpleSaga> _logger;
        
        public SimpleSaga(ILogger<SimpleSaga> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IMessageContext<StartSimpleSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting saga '{context.Message.CorrelationId}'...");
            
            var message = new ProcessSimpleSaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }
        
        public async Task HandleAsync(IMessageContext<ProcessSimpleSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"processing saga '{context.Message.CorrelationId}'...");
            
            var message = new SimpleSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public Task HandleAsync(IMessageContext<SimpleSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted(); 
            _logger.LogInformation($"saga '{context.Message.CorrelationId}' completed!");
            return Task.CompletedTask;
        }
    }
}
