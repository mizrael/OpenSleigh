using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Tests.Sagas
{
    public class ParentSagaState : SagaState
    {
        public ParentSagaState(Guid id) : base(id) { }
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
        private readonly Action<IMessageContext<ParentSagaCompleted>> _onCompleted;        
        private readonly ILogger<ParentSaga> _logger;
        
        public ParentSaga(Action<IMessageContext<ParentSagaCompleted>> onCompleted, ILogger<ParentSaga> logger, ParentSagaState state) : base(state)
        {
            _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            _logger = logger;
        }

        public async Task HandleAsync(IMessageContext<StartParentSaga> context, CancellationToken cancellationToken = default)
        {
            var message = new ProcessParentSaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public async Task HandleAsync(IMessageContext<ProcessParentSaga> context, CancellationToken cancellationToken = default)
        {
            var message = new StartChildSaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public async Task HandleAsync(IMessageContext<ChildSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            var message = new ParentSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public Task HandleAsync(IMessageContext<ParentSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"completing Parent Saga '{context.Message.CorrelationId}'");

            this.State.MarkAsCompleted();

            _onCompleted?.Invoke(context);

            return Task.CompletedTask;
        }
    }
}