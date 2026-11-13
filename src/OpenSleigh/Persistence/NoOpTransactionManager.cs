namespace OpenSleigh.Persistence;

/// <summary>
/// the transaction manager of a persistence that has no transaction to give around a pass of the outbox relay.
/// </summary>
public sealed class NoOpTransactionManager : ITransactionManager
{
    public ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<ITransaction>(NoOpTransaction.Instance);
}
