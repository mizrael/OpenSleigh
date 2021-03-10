using OpenSleigh.Core.Persistence;
using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Persistence.Cosmos
{
    public class CosmosTransactionManager : ITransactionManager
    {
        private readonly IMongoClient _client;
        private readonly ILogger<CosmosTransactionManager> _logger;
        private readonly IDbContext _dbContext;
        
        public CosmosTransactionManager(IMongoClient client, 
            ILogger<CosmosTransactionManager> logger, 
            IDbContext dbContext)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        {
            // TODO: as of today, Cosmos has no support for multi-document transactions across collections.
            // https://docs.microsoft.com/en-ca/azure/cosmos-db/mongodb-feature-support-40#transactions
            ITransaction transaction = new NullTransaction();
            return Task.FromResult(transaction);
        }
    }
}