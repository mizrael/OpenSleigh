using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record StartTransfer(string CorrelationId) : IMessage, IHasCorrelationId;

public record TransferExecuted : IMessage;

public class TransferState
{
    public int Executions { get; set; }
}

/// <summary>
/// a transfer that is executed, then confirmed by the message its execution publishes.
/// </summary>
public class TransferSaga(ISagaInstance<TransferState> context, HandlerRuns runs) :
    Saga<TransferState>(context),
    IStartedBy<StartTransfer>,
    IHandleMessage<TransferExecuted>
{
    public ValueTask HandleAsync(IMessageContext<StartTransfer> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Context.State.Executions++;
        Publish(new TransferExecuted());
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<TransferExecuted> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }
}
