using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record StartPayout(string CorrelationId) : IMessage, IHasCorrelationId;

public record PayoutExecuted : IMessage;

public class PayoutState
{
    public int Executions { get; set; }
}

/// <summary>a payout that is executed, then confirmed by the message its execution publishes.</summary>
public class PayoutSaga(ISagaInstance<PayoutState> context) :
    Saga<PayoutState>(context),
    IStartedBy<StartPayout>,
    IHandleMessage<PayoutExecuted>
{
    public ValueTask HandleAsync(IMessageContext<StartPayout> context, CancellationToken cancellationToken = default)
    {
        Context.State.Executions++;
        Publish(new PayoutExecuted());
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<PayoutExecuted> context, CancellationToken cancellationToken = default)
    {
        Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }
}
