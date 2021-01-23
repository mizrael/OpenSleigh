using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    internal class SqlTransaction : ITransaction
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
    }
}