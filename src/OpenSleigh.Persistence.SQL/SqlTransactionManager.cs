using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    internal class SqlTransactionManager : ITransactionManager
    {
        private readonly SagaDbContext _dbContext;

        public SqlTransactionManager(SagaDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            return new SqlTransaction(transaction);
        }
    }
}