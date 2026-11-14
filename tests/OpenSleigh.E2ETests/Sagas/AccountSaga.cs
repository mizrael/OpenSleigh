using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record OpenAccount(string CorrelationId) : IMessage, IHasCorrelationId;

public record Deposit(string CorrelationId, int Amount) : IMessage, IHasCorrelationId;

public record Withdraw(string CorrelationId, int Amount) : IMessage, IHasCorrelationId;

public class AccountState
{
    public int Balance { get; set; }
    public int Movements { get; set; }
}

/// <summary>
/// an account whose balance is the sum of its movements: a movement that is lost, or applied twice, shows in the balance.
/// </summary>
public class AccountSaga(ISagaInstance<AccountState> context, HandlerRuns runs) :
    Saga<AccountState>(context),
    IStartedBy<OpenAccount>,
    IHandleMessage<Deposit>,
    IHandleMessage<Withdraw>
{
    public ValueTask HandleAsync(IMessageContext<OpenAccount> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<Deposit> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Apply(context.Message.Amount);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<Withdraw> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Apply(-context.Message.Amount);
        return ValueTask.CompletedTask;
    }

    private void Apply(int amount)
    {
        Context.State.Balance += amount;
        Context.State.Movements++;
    }
}
