using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

internal class FakeSagaWithState : 
    Saga<int>, 
    IStartedBy<FakeSagaStarter>
{
    public FakeSagaWithState(ISagaInstance<int> context) : base(context)
    {
    }

    public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}