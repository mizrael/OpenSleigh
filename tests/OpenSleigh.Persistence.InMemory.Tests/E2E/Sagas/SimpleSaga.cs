using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Persistence.InMemory.Tests.E2E
{
    internal class SimpleSagaState : SagaState
    {
        public SimpleSagaState(Guid id) : base(id)
        {
        }
    }

    internal record StartSimpleSaga(Guid Id, Guid CorrelationId) : ICommand;
    
    internal class SimpleSaga : Saga<SimpleSagaState>, IStartedBy<StartSimpleSaga>
    {
        private readonly Action<StartSimpleSaga> _onStart;

        public SimpleSaga(Action<StartSimpleSaga> onStart)
        {
            _onStart = onStart;
        }

        public Task HandleAsync(IMessageContext<StartSimpleSaga> context, CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(context.Message);
            return Task.CompletedTask;
        }
    }
}