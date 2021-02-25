using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Tests.Sagas
{
    public class CompensatingSagaState : SagaState
    {
        public CompensatingSagaState(Guid id) : base(id)
        {
        }
    }

    public record StartCompensatingSaga(Guid Id, Guid CorrelationId) : ICommand
    {
        public int Foo { get; init; }
        public string Bar { get; init; }
        public Guid Baz => this.CorrelationId;
    }

    public class CompensatingSaga : 
        Saga<CompensatingSagaState>
        , IStartedBy<StartCompensatingSaga>
        , ICompensateMessage<StartCompensatingSaga>
    {
        private readonly Action<IMessageContext<StartCompensatingSaga>> _onStart;
        private readonly Action<ICompensationContext<StartCompensatingSaga>> _onCompensate;

        public CompensatingSaga(Action<IMessageContext<StartCompensatingSaga>> onStart,
                                Action<ICompensationContext<StartCompensatingSaga>> onCompensate)
        {
            _onStart = onStart;
            _onCompensate = onCompensate;
        }

        public Task HandleAsync(IMessageContext<StartCompensatingSaga> context, CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(context);
            return Task.CompletedTask;
        }

        public Task CompensateAsync(ICompensationContext<StartCompensatingSaga> context, CancellationToken cancellationToken = default)
        {
            _onCompensate?.Invoke(context);
            return Task.CompletedTask;
        }
    }
}