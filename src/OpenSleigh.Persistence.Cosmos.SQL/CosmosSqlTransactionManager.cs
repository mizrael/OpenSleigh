using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Persistence;

[assembly: InternalsVisibleTo("OpenSleigh.Persistence.Cosmos.SQL.Tests")]
namespace OpenSleigh.Persistence.Cosmos.SQL
{
    internal class CosmosSqlTransactionManager : ITransactionManager
    {
        private readonly ISagaDbContext _dbContext;

        public CosmosSqlTransactionManager(ISagaDbContext dbContext)
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