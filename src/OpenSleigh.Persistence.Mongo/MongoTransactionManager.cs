namespace OpenSleigh.Persistence.Mongo;

/// <summary>
/// the outbox relay asks for a transaction around each pass. On MongoDB there is none to give:
/// a relay claims the messages it reads instead, and whatever it does not delete is read again when the claim expires.
/// </summary>
internal sealed class MongoTransactionManager : ITransactionManager
{
    public ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<ITransaction>(NoOpTransaction.Instance);
}
