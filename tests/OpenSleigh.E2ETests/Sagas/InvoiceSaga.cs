using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record IssueInvoice(string CorrelationId) : IMessage, IHasCorrelationId;

public record SendInvoice(string CorrelationId) : IMessage, IHasCorrelationId;

/// <summary>called while an invoice is being sent, before the saga is done with the message: it can keep the handler waiting.</summary>
public delegate Task WhileSendingInvoice(IMessageContext<SendInvoice> context, CancellationToken cancellationToken);

/// <summary>
/// an invoice that is issued, then sent: two messages, the second published by the saga itself.
/// </summary>
public class InvoiceSaga :
    Saga,
    IStartedBy<IssueInvoice>,
    IHandleMessage<SendInvoice>
{
    private readonly WhileSendingInvoice _whileSending;
    private readonly Action<IMessageContext<SendInvoice>> _onSent;

    public InvoiceSaga(
        WhileSendingInvoice whileSending,
        Action<IMessageContext<SendInvoice>> onSent,
        ISagaInstance context) : base(context)
    {
        _whileSending = whileSending ?? throw new ArgumentNullException(nameof(whileSending));
        _onSent = onSent ?? throw new ArgumentNullException(nameof(onSent));
    }

    public ValueTask HandleAsync(IMessageContext<IssueInvoice> context, CancellationToken cancellationToken = default)
    {
        Publish(new SendInvoice(context.CorrelationId));
        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(IMessageContext<SendInvoice> context, CancellationToken cancellationToken = default)
    {
        await _whileSending(context, cancellationToken);

        Context.MarkAsCompleted();

        _onSent(context);
    }
}
