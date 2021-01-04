using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Tests.Unit
{
    public class DummySagaState : SagaState
    {
        public DummySagaState(Guid id) : base(id)
        {
        }
    }

    public record StartDummySaga(Guid Id, Guid CorrelationId) : ICommand
    {
        public static StartDummySaga New() => new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());
    }

    public record DummySagaStarted(Guid Id, Guid CorrelationId) : ICommand
    {
        public static DummySagaStarted New() => new DummySagaStarted(Guid.NewGuid(), Guid.NewGuid());
    }

    public class DummySaga :
        Saga<DummySagaState>,
        IStartedBy<StartDummySaga>,
        IHandleMessage<DummySagaStarted>
    {
        public virtual Task HandleAsync(IMessageContext<StartDummySaga> context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public virtual Task HandleAsync(IMessageContext<DummySagaStarted> context, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
