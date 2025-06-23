using Microsoft.EntityFrameworkCore.Storage;

namespace OpenSleigh.Persistence.SQL;

internal sealed class SqlTransaction : ITransaction
{
    private readonly IDbContextTransaction _transaction;

    public SqlTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public ValueTask CommitAsync(CancellationToken cancellationToken = default) =>
        new ValueTask(_transaction.CommitAsync(cancellationToken));

    public ValueTask RollbackAsync(CancellationToken cancellationToken = default) =>
        new ValueTask(_transaction.RollbackAsync(cancellationToken));

    public ValueTask DisposeAsync()
    => _transaction.DisposeAsync();
}