using System.Text.Json.Serialization;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests;

[method: JsonConstructor]
public record StartMultiStartSaga(int Foo, string CorrelationId, string RequestId) : IIdempotentMessage, IHasCorrelationId
{
    public StartMultiStartSaga(int foo, string correlationId) :
        this(foo, correlationId, Guid.NewGuid().ToString("N")){ }

    public IEnumerable<object> GetIdempotencyComponents() => [];
}

[method: JsonConstructor]
public record AlsoStartMultiStartSaga(string Bar, string CorrelationId, string RequestId) : IIdempotentMessage, IHasCorrelationId
{
    public AlsoStartMultiStartSaga(string bar, string correlationId) :
        this(bar, correlationId, Guid.NewGuid().ToString("N")){ }

    public IEnumerable<object> GetIdempotencyComponents() => [];
}

public class MultiStartSaga :
    Saga<MultiStartSagaState>,
    IStartedBy<StartMultiStartSaga>,
    IStartedBy<AlsoStartMultiStartSaga>
{
    private readonly Action<IMessageContext<StartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> _onStart;
    private readonly Action<IMessageContext<AlsoStartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> _onAlsoStart;

    public MultiStartSaga(
        Action<IMessageContext<StartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onStart,
        Action<IMessageContext<AlsoStartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onAlsoStart,
        ISagaInstance<MultiStartSagaState> context
    ) :  base(context)
    {
        _onStart = onStart;
        _onAlsoStart = onAlsoStart;
    }

    public ValueTask HandleAsync(IMessageContext<StartMultiStartSaga> context, CancellationToken cancellationToken = default)
    {
        _onStart?.Invoke(context, Context);
        Context.State.Foo = context.Message.Foo;
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<AlsoStartMultiStartSaga> context, CancellationToken cancellationToken = default)
    {
        _onAlsoStart?.Invoke(context, Context);
        Context.State.Bar = context.Message.Bar;
        return ValueTask.CompletedTask;
    }
}

public record MultiStartSagaState
{
    public int Foo { get; set; }
    public string Bar { get; set; }
}