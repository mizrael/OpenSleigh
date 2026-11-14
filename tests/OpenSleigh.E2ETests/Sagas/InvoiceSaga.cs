using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record IssueInvoice(string CorrelationId) : IMessage, IHasCorrelationId;

public record SendInvoiceEmail : IMessage;

public class InvoiceState
{
    public int Issued { get; set; }
    public int EmailsSent { get; set; }
}

/// <summary>
/// an invoice that is issued once and mailed once: a message handled twice shows as a second issue,
/// and as a second email, which is a message of its own.
/// </summary>
public class InvoiceSaga(ISagaInstance<InvoiceState> context, HandlerRuns runs) :
    Saga<InvoiceState>(context),
    IStartedBy<IssueInvoice>,
    IHandleMessage<SendInvoiceEmail>
{
    public ValueTask HandleAsync(IMessageContext<IssueInvoice> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Context.State.Issued++;
        Publish(new SendInvoiceEmail());
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<SendInvoiceEmail> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Context.State.EmailsSent++;
        return ValueTask.CompletedTask;
    }
}
