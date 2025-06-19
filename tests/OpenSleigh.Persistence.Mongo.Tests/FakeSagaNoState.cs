using OpenSleigh.Transport;

namespace OpenSleigh.Persistence.Mongo.Tests;

public class FakeSagaNoState : ISaga, IStartedBy<FakeMessage>
{
    public FakeSagaNoState(ISagaInstance  context)
    {
        this.Context = context;
    }

    public ISagaInstance  Context { get; }

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

    ISagaInstance  ISaga.Context => throw new System.NotImplementedException();

    public ValueTask HandleAsync(IMessageContext<FakeMessage> messageContext, CancellationToken cancellationToken = default)
    {
        throw new System.NotImplementedException();
    }
}