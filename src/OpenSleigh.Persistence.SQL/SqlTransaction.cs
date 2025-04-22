using Microsoft.EntityFrameworkCore.Storage;

namespace OpenSleigh.Persistence.SQL;

internal sealed class SqlTransaction : ITransaction
{
    private readonly IDbContextTransaction _transaction;

    public SqlTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        _transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _transaction.RollbackAsync(cancellationToken);

    public ValueTask DisposeAsync()
    => _transaction.DisposeAsync();
}