using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Samples.Console.Sagas
{
    public class ParentSagaState : SagaState{
        public ParentSagaState(Guid id) : base(id){}
    }

    public record StartParentSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ProcessParentSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ParentSagaCompleted(Guid Id, Guid CorrelationId) : IEvent { }

    public class ParentSaga :
        Saga<ParentSagaState>,
        IStartedBy<StartParentSaga>,
        IHandleMessage<ProcessParentSaga>,
        IHandleMessage<ChildSagaCompleted>,
        IHandleMessage<ParentSagaCompleted>
    {
        private readonly IMessageBus _bus;
        private readonly ILogger<ParentSaga> _logger;

        private readonly Random _random = new Random();

        public ParentSaga(ILogger<ParentSaga> logger, IMessageBus bus)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }
        
        public async Task HandleAsync(IMessageContext<StartParentSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting parent saga '{context.Message.CorrelationId}'...");
            
            var message = new ProcessParentSaga(Guid.NewGuid(), context.Message.CorrelationId);
            await _bus.PublishAsync(message);
        }
        
        public async Task HandleAsync(IMessageContext<ProcessParentSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting child saga from parent saga '{context.Message.CorrelationId}'...");
            
            var message = new StartChildSaga(Guid.NewGuid(), context.Message.CorrelationId);
            await _bus.PublishAsync(message);
        }

        public async Task HandleAsync(IMessageContext<ChildSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"child saga completed, finalizing parent saga '{context.Message.CorrelationId}'...");

            await Task.Delay(TimeSpan.FromSeconds(_random.Next(1, 5)), cancellationToken);
            
            var message = new ParentSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            await _bus.PublishAsync(message);
        }

        public async Task HandleAsync(IMessageContext<ParentSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"parent saga '{context.Message.CorrelationId}' completed!");
        }
    }
}
