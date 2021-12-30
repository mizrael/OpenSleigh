using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Tests.Sagas
{
    public record DummyEvent(Guid Id, Guid CorrelationId) : IEvent;
    
    public record EventSagaState1 : SagaState
    {
        public EventSagaState1(Guid id) : base(id)
        {
        }
    }

    public class EventSaga1 : Saga<EventSagaState1>, 
        IStartedBy<DummyEvent>
    {
        private readonly Action<IMessageContext<DummyEvent>> _onStart;

        public EventSaga1(Action<IMessageContext<DummyEvent>> onStart, EventSagaState1 state) : base(state)
        {
            _onStart = onStart;
        }

        public Task HandleAsync(IMessageContext<DummyEvent> context, CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(context);
            return Task.CompletedTask;
        }
    }

    public record EventSagaState2 : SagaState
    {
        public EventSagaState2(Guid id) : base(id)
        {
        }
    }

    public class EventSaga2 : Saga<EventSagaState2>,
        IStartedBy<DummyEvent>
    {
        private readonly Action<IMessageContext<DummyEvent>> _onStart;

        public EventSaga2(Action<IMessageContext<DummyEvent>> onStart, EventSagaState2 state) : base(state)    
        {
            _onStart = onStart;
        }

        public Task HandleAsync(IMessageContext<DummyEvent> context, CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(context);
            return Task.CompletedTask;
        }
    }
}