using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record OpenTransfer(Guid TransferId, string CorrelationId, decimal Amount) : IMessage, IHasCorrelationId;

public record DebitAccount(Guid TransferId, decimal Amount) : IMessage;

public record CreditAccount(Guid TransferId, decimal Amount) : IMessage;

public record TransferState
{
    public Guid TransferId { get; set; }
    public decimal Amount { get; set; }
    public decimal Debited { get; set; }
    public decimal Credited { get; set; }

    /// <summary>how many messages of another transfer this one was given.</summary>
    public int MessagesOfOtherTransfers { get; set; }
}

/// <summary>
/// a money transfer in three steps, each started by the message the previous one published.
/// Every message says which transfer it belongs to, so that one handled by another instance shows in the state.
/// </summary>
public class TransferSaga :
    Saga<TransferState>,
    IStartedBy<OpenTransfer>,
    IHandleMessage<DebitAccount>,
    IHandleMessage<CreditAccount>
{
    private readonly Action<IMessageContext<CreditAccount>> _onCompleted;

    public TransferSaga(Action<IMessageContext<CreditAccount>> onCompleted, ISagaInstance<TransferState> context) : base(context)
    {
        _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
    }

    public ValueTask HandleAsync(IMessageContext<OpenTransfer> context, CancellationToken cancellationToken = default)
    {
        var transfer = context.Message;
        Context.State.TransferId = transfer.TransferId;
        Context.State.Amount = transfer.Amount;

        Publish(new DebitAccount(transfer.TransferId, transfer.Amount));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<DebitAccount> context, CancellationToken cancellationToken = default)
    {
        var debit = context.Message;
        CountWhenOfAnotherTransfer(debit.TransferId);
        Context.State.Debited += debit.Amount;

        Publish(new CreditAccount(Context.State.TransferId, Context.State.Amount));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<CreditAccount> context, CancellationToken cancellationToken = default)
    {
        var credit = context.Message;
        CountWhenOfAnotherTransfer(credit.TransferId);
        Context.State.Credited += credit.Amount;
        Context.MarkAsCompleted();

        _onCompleted(context);

        return ValueTask.CompletedTask;
    }

    private void CountWhenOfAnotherTransfer(Guid transferId)
    {
        if (transferId != Context.State.TransferId)
            Context.State.MessagesOfOtherTransfers++;
    }
}
