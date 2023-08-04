using Microsoft.Extensions.Logging;
using OpenSleigh.Samples.Sample2.Common.Messages;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Samples.Sample2.Worker.Sagas
{
    public record ParentSagaState;

    public record ProcessParentSaga(Guid Id, Guid CorrelationId) : IMessage { }

    public record ParentSagaCompleted(Guid Id, Guid CorrelationId) : IMessage { }

    public class ParentSaga :
        Saga<ParentSagaState>,
        IStartedBy<StartParentSaga>,
        IHandleMessage<ProcessParentSaga>,
        IHandleMessage<ChildSagaCompleted>,
        IHandleMessage<ParentSagaCompleted>
    {
        private readonly ILogger<ParentSaga> _logger;

        private readonly Random _random = new Random();

        public ParentSaga(ILogger<ParentSaga> logger,
            ISagaExecutionContext<ParentSagaState> context,
            ISerializer serializer) : base(context, serializer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async ValueTask HandleAsync(IMessageContext<StartParentSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting parent saga '{context.Message.CorrelationId}'...");
            
            var message = new ProcessParentSaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }
        
        public async ValueTask HandleAsync(IMessageContext<ProcessParentSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting child saga from parent saga '{context.Message.CorrelationId}'...");
            
            var message = new StartChildSaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public async ValueTask HandleAsync(IMessageContext<ChildSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"child saga completed, finalizing parent saga '{context.Message.CorrelationId}'...");

            await Task.Delay(TimeSpan.FromSeconds(_random.Next(1, 5)), cancellationToken);
            
            var message = new ParentSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public ValueTask HandleAsync(IMessageContext<ParentSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.Context.MarkAsCompleted(); 
            _logger.LogInformation($"parent saga '{context.Message.CorrelationId}' completed!");
            return ValueTask.CompletedTask;
        }
    }
}
