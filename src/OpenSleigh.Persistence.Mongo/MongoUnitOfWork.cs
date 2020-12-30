using OpenSleigh.Core.Persistence;
using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Mongo
{
    internal class MongoUnitOfWork : IUnitOfWork
    {
        private readonly MongoClient _client;

        public MongoUnitOfWork(MongoClient client, ISagaStateRepository sagaStatesRepository)
        {
            SagaStatesRepository = sagaStatesRepository ?? throw new ArgumentNullException(nameof(sagaStatesRepository));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ISagaStateRepository SagaStatesRepository { get; }

        public async Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transactionOptions = new TransactionOptions(ReadConcern.Majority, ReadPreference.Primary, WriteConcern.WMajority);
            var session = _client.StartSession();
            session.StartTransaction(transactionOptions);
            return new MongoTransaction(session);
        }
    }
}