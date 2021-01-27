using OpenSleigh.Core.Persistence;
using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Persistence.Mongo
{
    public class MongoTransactionManager : ITransactionManager
    {
        private readonly IMongoClient _client;
        private readonly ILogger<MongoTransactionManager> _logger;
        private readonly IDbContext _dbContext;
        
        public MongoTransactionManager(IMongoClient client, 
            ILogger<MongoTransactionManager> logger, 
            IDbContext dbContext)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transactionOptions = new TransactionOptions(ReadConcern.Majority, ReadPreference.Primary, WriteConcern.WMajority);

            ITransaction transaction;
            try
            {
                //the async overload might freeze if transactions are not supported
                var session = _client.StartSession();
                session.StartTransaction(transactionOptions);
                
                //TODO: I don't really like this coupling.
                transaction = _dbContext.Transaction = new MongoTransaction(session);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning($"unable to start MongoDB transaction : {ex.Message}");
                transaction = new NullTransaction();
            }

            return Task.FromResult(transaction);
        }
    }
}