using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Tests.Sagas
{
    public record DummySagaState : SagaState
    {
        public DummySagaState(Guid id) : base(id)
        {
        }
    }

    public record UnhandledMessage(Guid Id, Guid CorrelationId) : ICommand
    {
        public static UnhandledMessage New() => new UnhandledMessage(Guid.NewGuid(), Guid.NewGuid());
    }

    public record StartDummySaga(Guid Id, Guid CorrelationId) : ICommand
    {
        public static StartDummySaga New() => new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());
    }

    public record DummySagaStarted(Guid Id, Guid CorrelationId) : IEvent
    {
        public static DummySagaStarted New() => new DummySagaStarted(Guid.NewGuid(), Guid.NewGuid());
    }

    public class DummySaga :
        Saga<DummySagaState>,
        IStartedBy<StartDummySaga>,
        IHandleMessage<DummySagaStarted>
    {
        public DummySaga(DummySagaState state) : base(state)
        {
        }

        public virtual Task HandleAsync(IMessageContext<StartDummySaga> context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleAsync(IMessageContext<DummySagaStarted> context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void PublishTestWrapper<TM>(TM message) where TM : IMessage
            => base.Publish(message);
    }
}
