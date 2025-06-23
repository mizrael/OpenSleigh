namespace OpenSleigh.Persistence;

public interface ITransactionManager
{
    ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default);
}