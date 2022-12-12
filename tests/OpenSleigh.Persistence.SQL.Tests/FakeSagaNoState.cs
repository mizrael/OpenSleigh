using OpenSleigh.Messaging;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.SQL.Tests
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
}