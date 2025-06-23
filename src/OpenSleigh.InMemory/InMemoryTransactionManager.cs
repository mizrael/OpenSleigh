using OpenSleigh.Persistence;

namespace OpenSleigh.InMemory;

internal class InMemoryTransactionManager : ITransactionManager
{
    public ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
    => ValueTask.FromResult<ITransaction>(NoOpTransaction.Instance);
}