using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Samples.Console.Sagas
{
    public class ChildSagaState : SagaState{
        public ChildSagaState(Guid id) : base(id){}
    }

    public record StartChildSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ProcessChildSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ChildSagaCompleted(Guid Id, Guid CorrelationId) : IEvent { }

    public class ChildSaga :
        Saga<ChildSagaState>,
        IStartedBy<StartChildSaga>,
        IHandleMessage<ProcessChildSaga>
    {
        private readonly IMessageBus _bus;
        private readonly ILogger<ChildSaga> _logger;

        private readonly Random _random = new Random();

        public ChildSaga(ILogger<ChildSaga> logger, IMessageBus bus)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }
        
        public async Task HandleAsync(IMessageContext<StartChildSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting child saga '{context.Message.CorrelationId}'...");

            await Task.Delay(TimeSpan.FromSeconds(_random.Next(1, 5)), cancellationToken);

            var message = new ProcessChildSaga(Guid.NewGuid(), context.Message.CorrelationId);
            await _bus.PublishAsync(message, cancellationToken);
        }

        public async Task HandleAsync(IMessageContext<ProcessChildSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"processing child saga '{context.Message.CorrelationId}'...");
            
            await Task.Delay(TimeSpan.FromSeconds(_random.Next(1, 5)), cancellationToken);
            
            _logger.LogInformation($"child saga '{context.Message.CorrelationId}' completed!");

            var completedEvent = new ChildSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            await _bus.PublishAsync(completedEvent, cancellationToken);
        }
    }
}
