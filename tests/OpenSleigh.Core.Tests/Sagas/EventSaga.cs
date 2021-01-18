using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Tests.Sagas
{
    public class EventSagaState : SagaState
    {
        public EventSagaState(Guid id) : base(id)
        {
        }
    }

    public record DummyEvent(Guid Id, Guid CorrelationId) : IEvent;

    public class EventSaga1 : Saga<EventSagaState>, 
        IStartedBy<DummyEvent>
    {
        private readonly Action<DummyEvent> _onStart;

        public EventSaga1(Action<DummyEvent> onStart)
        {
            _onStart = onStart;
        }

        public Task HandleAsync(IMessageContext<DummyEvent> context, CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(context.Message);
            return Task.CompletedTask;
        }
    }

    public class EventSaga2 : Saga<EventSagaState>,
        IStartedBy<DummyEvent>
    {
        private readonly Action<DummyEvent> _onStart;

        public EventSaga2(Action<DummyEvent> onStart)
        {
            _onStart = onStart;
        }

        public Task HandleAsync(IMessageContext<DummyEvent> context, CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(context.Message);
            return Task.CompletedTask;
        }
    }
}