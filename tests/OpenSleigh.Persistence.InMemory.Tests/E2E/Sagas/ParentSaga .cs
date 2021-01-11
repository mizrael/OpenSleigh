using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Persistence.InMemory.Tests.E2E.Sagas
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
        private readonly Action<ParentSagaCompleted> _onCompleted;

        
        public ParentSaga(Action<ParentSagaCompleted> onCompleted)
        {
            _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        }

        public async Task HandleAsync(IMessageContext<StartParentSaga> context, CancellationToken cancellationToken = default)
        {
            var message = new ProcessParentSaga(Guid.NewGuid(), context.Message.CorrelationId);
            await this.Bus.PublishAsync(message, cancellationToken);
        }

        public async Task HandleAsync(IMessageContext<ProcessParentSaga> context, CancellationToken cancellationToken = default)
        {
            var message = new StartChildSaga(Guid.NewGuid(), context.Message.CorrelationId);
            await this.Bus.PublishAsync(message, cancellationToken);
        }

        public async Task HandleAsync(IMessageContext<ChildSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            var message = new ParentSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            await this.Bus.PublishAsync(message, cancellationToken);
        }

        public Task HandleAsync(IMessageContext<ParentSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted();

            _onCompleted?.Invoke(context.Message);

            return Task.CompletedTask;
        }
    }
}