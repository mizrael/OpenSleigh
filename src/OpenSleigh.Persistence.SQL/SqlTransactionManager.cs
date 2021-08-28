using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    public class SqlTransactionManager : ITransactionManager
    {
        private readonly ISagaDbContext _dbContext;

        public SqlTransactionManager(ISagaDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await _dbContext.StartTransactionAsync(cancellationToken);
            return transaction;
        }
    }
}