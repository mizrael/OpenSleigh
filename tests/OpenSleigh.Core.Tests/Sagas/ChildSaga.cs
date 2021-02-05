using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Tests.Sagas
{
    public class ChildSagaState : SagaState
    {
        public ChildSagaState(Guid id) : base(id) { }
    }

    public record StartChildSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ProcessChildSaga(Guid Id, Guid CorrelationId) : ICommand { }

    public record ChildSagaCompleted(Guid Id, Guid CorrelationId) : IEvent { }

    public class ChildSaga :
        Saga<ChildSagaState>,
        IStartedBy<StartChildSaga>,
        IHandleMessage<ProcessChildSaga>
    {

        public async Task HandleAsync(IMessageContext<StartChildSaga> context, CancellationToken cancellationToken = default)
        {
            var message = new ProcessChildSaga(Guid.NewGuid(), context.Message.CorrelationId);
            await this.Bus.PublishAsync(message, cancellationToken);
        }

        public async Task HandleAsync(IMessageContext<ProcessChildSaga> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted();
            
            var completedEvent = new ChildSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            await this.Bus.PublishAsync(completedEvent, cancellationToken);
        }
    }
}