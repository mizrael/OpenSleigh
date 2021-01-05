using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Persistence.Mongo.Utils;

namespace OpenSleigh.Persistence.Mongo.Messaging
{
    public class OutboxRepository : IOutboxRepository
    {
        private readonly IDbContext _dbContext;
        private readonly ISerializer _serializer;

        private enum MessageStatuses
        {
            Pending,
            Processed
        }

        public OutboxRepository(IDbContext dbContext, ISerializer serializer)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async Task<IEnumerable<IMessage>> ReadMessagesToProcess(CancellationToken cancellationToken = default)
        {
            var entities = await _dbContext.Outbox.Find(e => e.Status == MessageStatuses.Pending.ToString())
                .Skip(0)
                .Limit(10)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var messages = new List<IMessage>();
            foreach (var entity in entities)
            {
                var message = await _serializer.DeserializeAsync<IMessage>(entity.Data, cancellationToken);
                messages.Add(message);
            }

            return messages;
        }

        public Task MarkAsSentAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));

            return MarkAsSentAsyncCore(message, cancellationToken);
        }

        private async Task MarkAsSentAsyncCore(IMessage message, CancellationToken cancellationToken)
        {
            var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.Id, message.Id),
                filterBuilder.Eq(e => e.Status, MessageStatuses.Pending.ToString())
            );

            var update = Builders<Entities.OutboxMessage>.Update
                .Set(e => e.Status, MessageStatuses.Processed.ToString())
                .Set(e => e.PublishingDate, DateTime.UtcNow);

            var options = new UpdateOptions()
            {
                IsUpsert = false
            };

            var result = await _dbContext.Outbox.UpdateOneAsync(filter, update, options, cancellationToken)
                .ConfigureAwait(false);
            if (0 == result.MatchedCount)
                throw new ArgumentException($"message '{message.Id}' not found. Maybe it was already processed?");
        }

        public Task AppendAsync(IMessage message, ITransaction transaction = null, CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));

            return AppendAsyncCore(message, transaction, cancellationToken);
        }

        public Task CleanProcessedAsync(CancellationToken cancellationToken = default) =>
            _dbContext.Outbox.DeleteManyAsync(e => e.Status == MessageStatuses.Processed.ToString(), cancellationToken);

        private async Task AppendAsyncCore(IMessage message, ITransaction transaction, CancellationToken cancellationToken)
        {
            var data = await _serializer.SerializeAsync(message, cancellationToken);
            var entity =
                new Entities.OutboxMessage(message.Id, data, message.GetType().FullName, MessageStatuses.Pending.ToString());

            if (transaction is MongoTransaction mongoTransaction && mongoTransaction.Session is not null)
                await _dbContext.Outbox.InsertOneAsync(mongoTransaction.Session, entity, null, cancellationToken)
                    .ConfigureAwait(false);
            else
                await _dbContext.Outbox.InsertOneAsync(entity, null, cancellationToken)
                    .ConfigureAwait(false);
        }
    }
}