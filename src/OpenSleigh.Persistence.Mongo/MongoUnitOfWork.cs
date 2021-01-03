using OpenSleigh.Core.Persistence;
using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Persistence.Mongo
{
    internal class MongoUnitOfWork : IUnitOfWork
    {
        private readonly MongoClient _client;
        private readonly ILogger<MongoUnitOfWork> _logger;

        public MongoUnitOfWork(MongoClient client, ISagaStateRepository sagaStatesRepository, ILogger<MongoUnitOfWork> logger)
        {
            SagaStatesRepository = sagaStatesRepository ?? throw new ArgumentNullException(nameof(sagaStatesRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ISagaStateRepository SagaStatesRepository { get; }

        public async Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transactionOptions = new TransactionOptions(ReadConcern.Majority, ReadPreference.Primary, WriteConcern.WMajority);

            try
            {
                var session = _client.StartSession();
                session.StartTransaction(transactionOptions);
                return new MongoTransaction(session);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning($"unable to start MongoDB transaction : {ex.Message}");
                return new NullTransaction();
            }
        }
    }
}