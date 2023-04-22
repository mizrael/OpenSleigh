using OpenSleigh.Transport;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Mongo.Tests
{
    public class FakeSagaNoState : ISaga, IStartedBy<FakeMessage>
    {
        public FakeSagaNoState(ISagaExecutionContext context)
        {
            this.Context = context;
        }

        public ISagaExecutionContext Context { get; }

        public ValueTask HandleAsync(IMessageContext<FakeMessage> messageContext, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }

    public class FakeSagaWithState : ISaga<DummyState>, IStartedBy<FakeMessage>
    {
        public FakeSagaWithState(ISagaExecutionContext<DummyState> context)
        {
            this.Context = context;
        }

        public ISagaExecutionContext<DummyState> Context { get; }

        ISagaExecutionContext ISaga.Context => throw new System.NotImplementedException();

        public ValueTask HandleAsync(IMessageContext<FakeMessage> messageContext, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}