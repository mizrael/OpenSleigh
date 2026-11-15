using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record OpenTransfer(Guid TransferId, string CorrelationId, decimal Amount) : IMessage, IHasCorrelationId;

public record DebitAccount(Guid TransferId, decimal Amount) : IMessage;

public record SettleTransfer(Guid TransferId, string CorrelationId, decimal Amount) : IMessage, IHasCorrelationId;

public record TransferState
{
    public Guid TransferId { get; set; }
    public decimal Amount { get; set; }
    public decimal Debited { get; set; }
    public decimal Settled { get; set; }

    /// <summary>how many messages of another transfer this one was given.</summary>
    public int MessagesOfOtherTransfers { get; set; }
}

/// <summary>
/// a money transfer: it debits the account as soon as it is opened, then stays in flight until the settlement
/// comes from outside. Every message says which transfer it belongs to, and how much it moves,
/// so that one handled by another instance shows in the state.
/// </summary>
public class TransferSaga(ISagaInstance<TransferState> context) :
    Saga<TransferState>(context),
    IStartedBy<OpenTransfer>,
    IHandleMessage<DebitAccount>,
    IHandleMessage<SettleTransfer>
{
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

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<SettleTransfer> context, CancellationToken cancellationToken = default)
    {
        var settlement = context.Message;
        CountWhenOfAnotherTransfer(settlement.TransferId);
        Context.State.Settled += settlement.Amount;
        Context.MarkAsCompleted();

        return ValueTask.CompletedTask;
    }

    private void CountWhenOfAnotherTransfer(Guid transferId)
    {
        if (transferId != Context.State.TransferId)
            Context.State.MessagesOfOtherTransfers++;
    }
}
