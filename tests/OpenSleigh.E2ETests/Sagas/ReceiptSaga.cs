using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record IssueReceipt(string CorrelationId) : IMessage, IHasCorrelationId;

public record SendReceiptEmail : IMessage;

public class ReceiptState
{
    public int Issued { get; set; }
    public int EmailsSent { get; set; }
}

/// <summary>
/// a receipt that is issued once and mailed once: a message handled twice shows as a second issue,
/// and as a second email, which is a message of its own.
/// </summary>
public class ReceiptSaga(ISagaInstance<ReceiptState> context) :
    Saga<ReceiptState>(context),
    IStartedBy<IssueReceipt>,
    IHandleMessage<SendReceiptEmail>
{
    public ValueTask HandleAsync(IMessageContext<IssueReceipt> context, CancellationToken cancellationToken = default)
    {
        Context.State.Issued++;
        Publish(new SendReceiptEmail());
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<SendReceiptEmail> context, CancellationToken cancellationToken = default)
    {
        Context.State.EmailsSent++;
        return ValueTask.CompletedTask;
    }
}
