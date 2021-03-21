using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.Cosmos.SQL
{
    internal sealed class CosmosSqlTransaction : ITransaction, IDisposable
    {
        private IDbContextTransaction _transaction;

        public CosmosSqlTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            _transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            _transaction.RollbackAsync(cancellationToken);

        public void Dispose()
        {
            _transaction?.Dispose();
            _transaction = null;
        }
    }
}