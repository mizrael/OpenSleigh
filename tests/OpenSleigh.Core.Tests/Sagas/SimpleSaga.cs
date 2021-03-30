using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Tests.Sagas
{
    public class SimpleSagaState : SagaState
    {
        public SimpleSagaState(Guid id) : base(id)
        {
        }
    }

    public record StartSimpleSaga(Guid Id, Guid CorrelationId) : ICommand
    {
        public int Foo { get; init; }
        public string Bar { get; init; }
        public Guid Baz => this.CorrelationId;
    }

    public class SimpleSaga : Saga<SimpleSagaState>, IStartedBy<StartSimpleSaga>
    {
        private readonly Action<IMessageContext<StartSimpleSaga>> _onStart;

        public SimpleSaga(Action<IMessageContext<StartSimpleSaga>> onStart)
        {
            _onStart = onStart;
        }

        public Task HandleAsync(IMessageContext<StartSimpleSaga> context, CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(context);
            return Task.CompletedTask;
        }
    }
}