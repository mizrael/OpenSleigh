using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record IdempotentMessage(string IdempotencyKey) : IMessage, IHasIdempotencyKey;

public class IdempotentSaga :
    Saga,
    IStartedBy<IdempotentMessage>
{
    private readonly Action<IMessageContext<IdempotentMessage>> _onStart;

    public IdempotentSaga(
        Action<IMessageContext<IdempotentMessage>> onStart,
        ISagaExecutionContext context) : base(context)
    {
        _onStart = onStart;
    }

    public ValueTask HandleAsync(IMessageContext<IdempotentMessage> context, CancellationToken cancellationToken = default)
    {
        _onStart?.Invoke(context);
        return ValueTask.CompletedTask;
    }
}
