using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Persistence.Mongo
{
    public record MongoOutboxRepositoryOptions(TimeSpan LockMaxDuration)
    {
        public static readonly MongoOutboxRepositoryOptions Default = new (TimeSpan.FromMinutes(1));
    }
    
    public class MongoOutboxRepository : IOutboxRepository
    {
        private readonly IDbContext _dbContext;
        private readonly ISerializer _serializer;
        private readonly MongoOutboxRepositoryOptions _options;

        private enum MessageStatuses
        {
            Pending,
            Processed
        }

        public MongoOutboxRepository(IDbContext dbContext, ISerializer serializer, MongoOutboxRepositoryOptions options)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<IEnumerable<IMessage>> ReadMessagesToProcess(CancellationToken cancellationToken = default)
        {
            var entities = await _dbContext.Outbox.Find(e => 
                    e.Status == MessageStatuses.Pending.ToString() && 
                    (e.LockId == null || e.LockTime > DateTime.UtcNow - _options.LockMaxDuration))
                .Skip(0)
                .Limit(10)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (entities is null)
                return Enumerable.Empty<IMessage>();

            var messages = new List<IMessage>();
            foreach (var entity in entities)
            {
                var message = await _serializer.DeserializeAsync<IMessage>(entity.Data, cancellationToken);
                messages.Add(message);
            }

            return messages;
        }

        public Task ReleaseAsync(IMessage message, Guid lockId, CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));

            return ReleaseAsyncCore(message, lockId, cancellationToken);
        }

        private async Task ReleaseAsyncCore(IMessage message, Guid lockId, CancellationToken cancellationToken)
        {
            var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.Id, message.Id),
                filterBuilder.Eq(e => e.LockId, lockId),
                filterBuilder.Eq(e => e.Status, MessageStatuses.Pending.ToString())
            );

            var update = Builders<Entities.OutboxMessage>.Update
                .Set(e => e.Status, MessageStatuses.Processed.ToString())
                .Set(e => e.LockId, null)
                .Set(e => e.LockTime, null)
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

        public async Task CleanProcessedAsync(CancellationToken cancellationToken = default)
        {
            await _dbContext.Outbox.DeleteManyAsync(e => e.Status == MessageStatuses.Processed.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<Guid> LockAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));

            return LockAsyncCore(message, cancellationToken);
        }

        private async Task<Guid> LockAsyncCore(IMessage message, CancellationToken cancellationToken)
        {
            var lockId = Guid.NewGuid();

            var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.Id, message.Id),
                filterBuilder.Eq(e => e.Status, MessageStatuses.Pending.ToString()),
                filterBuilder.Or(
                    filterBuilder.Eq(e => e.LockId, null),
                    filterBuilder.Lt(e => e.LockTime, DateTime.UtcNow - _options.LockMaxDuration)
                )
            );
            var update = Builders<Entities.OutboxMessage>.Update
                .Set(e => e.LockId, lockId)
                .Set(e => e.LockTime, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<Entities.OutboxMessage>()
            {
                IsUpsert = false,
                ReturnDocument = ReturnDocument.After
            };

            var lockedMessage = await _dbContext.Outbox
                .FindOneAndUpdateAsync(filter, update, options, cancellationToken)
                .ConfigureAwait(false);
            if (null == lockedMessage)
                throw new LockException($"message '{message.Id}' is already locked");

            return lockId;
        }
    }
}