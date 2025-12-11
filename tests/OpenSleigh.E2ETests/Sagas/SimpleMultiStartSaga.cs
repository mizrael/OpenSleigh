using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests;

public record StartSimpleMultiStartSaga(int Foo, string CorrelationId) : IMessage, IHasCorrelationId;

public record AlsoStartSimpleMultiStartSaga(string Bar, string CorrelationId) : IMessage, IHasCorrelationId;

public class SimpleMultiStartSaga :
    Saga<SimpleMultiStartSagaState>,
    IStartedBy<StartSimpleMultiStartSaga>,
    IStartedBy<AlsoStartSimpleMultiStartSaga>
{
    private readonly Action<IMessageContext<StartSimpleMultiStartSaga>, SimpleMultiStartSagaState> _onStart;
    private readonly Action<IMessageContext<AlsoStartSimpleMultiStartSaga>> _onAlsoStart;

    public SimpleMultiStartSaga(
        Action<IMessageContext<StartSimpleMultiStartSaga>, SimpleMultiStartSagaState> onStart,
        Action<IMessageContext<AlsoStartSimpleMultiStartSaga>> onAlsoStart,
        ISagaInstance<SimpleMultiStartSagaState> context
    ) :  base(context)
    {
        _onStart = onStart;
        _onAlsoStart = onAlsoStart;
    }

    public ValueTask HandleAsync(IMessageContext<StartSimpleMultiStartSaga> context, CancellationToken cancellationToken = default)
    {
        _onStart?.Invoke(context, Context.State);
        Context.State.Foo = context.Message.Foo;
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<AlsoStartSimpleMultiStartSaga> context, CancellationToken cancellationToken = default)
    {
        _onAlsoStart?.Invoke(context);
        Context.State.Bar = context.Message.Bar;
        return ValueTask.CompletedTask;
    }
}

public record SimpleMultiStartSagaState
{
    public int Foo { get; set; }
    public string Bar { get; set; }
}