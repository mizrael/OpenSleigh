using OpenSleigh.Messaging;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests
{
    internal class FakeSaga : 
        Saga, 
        IStartedBy<FakeSagaStarter>,
        IHandleMessage<FakeSagaMessage>
    {
        public FakeSaga(ISagaExecutionContext context, ISerializer serializer) : base(context, serializer)
        {
        }

        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask HandleAsync(IMessageContext<FakeSagaMessage> messageContext, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}