using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record IssueInvoice(string CorrelationId) : IMessage, IHasCorrelationId;

public record SendInvoice(string CorrelationId) : IMessage, IHasCorrelationId;

/// <summary>
/// an invoice that is issued, then sent: two messages, the second published by the saga itself.
/// </summary>
public class InvoiceSaga(ISagaInstance context) :
    Saga(context),
    IStartedBy<IssueInvoice>,
    IHandleMessage<SendInvoice>
{
    public ValueTask HandleAsync(IMessageContext<IssueInvoice> context, CancellationToken cancellationToken = default)
    {
        Publish(new SendInvoice(context.CorrelationId));
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<SendInvoice> context, CancellationToken cancellationToken = default)
    {
        Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }
}
