using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record IdempotentMessage(string RequestId, string CorrelationId, int Foo) : IIdempotentMessage, IHasCorrelationId
{
    public IEnumerable<object> GetIdempotencyComponents()
    {
        yield return this.Foo;
    }
}

public class IdempotentSaga :
    Saga,
    IStartedBy<IdempotentMessage>
{
    private readonly Action<IMessageContext<IdempotentMessage>> _onStart;

    public IdempotentSaga(
        Action<IMessageContext<IdempotentMessage>> onStart,
        ISagaInstance  context) : base(context)
    {
        _onStart = onStart;
    }

    public ValueTask HandleAsync(IMessageContext<IdempotentMessage> context, CancellationToken cancellationToken = default)
    {
        _onStart?.Invoke(context);
        return ValueTask.CompletedTask;
    }
}
