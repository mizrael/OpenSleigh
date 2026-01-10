using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

internal class FakeSaga : 
    Saga, 
    IStartedBy<FakeSagaStarter>,
    IStartedBy<OtherFakeSagaStarter>,
    IHandleMessage<FakeSagaMessage>
{
    public FakeSaga(ISagaInstance context) : base(context)
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

    public ValueTask HandleAsync(IMessageContext<OtherFakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
