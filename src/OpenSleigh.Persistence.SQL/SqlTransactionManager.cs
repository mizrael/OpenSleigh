using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Persistence;

[assembly: InternalsVisibleTo("OpenSleigh.Persistence.SQL.Tests")]
namespace OpenSleigh.Persistence.SQL
{
    internal class SqlTransactionManager : ITransactionManager
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