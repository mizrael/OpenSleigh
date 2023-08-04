using MongoDB.Driver;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.Mongo
{
    public record MongoOutboxRepositoryOptions(TimeSpan LockMaxDuration)
    {
        public static readonly MongoOutboxRepositoryOptions Default = new(TimeSpan.FromMinutes(1));
    }

    public class MongoOutboxRepository : IOutboxRepository
    {
        private readonly IDbContext _dbContext;
        private readonly MongoOutboxRepositoryOptions _options;
        private readonly ITypeResolver _typeResolver;

        public MongoOutboxRepository(
            IDbContext dbContext,
            MongoOutboxRepositoryOptions options,
            ITypeResolver typeResolver)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        public ValueTask AppendAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            return AppendAsyncCore(messages, cancellationToken);
        }

        private async ValueTask AppendAsyncCore(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
        {
            var entities = messages.Select(message => Entities.OutboxMessage.Create(message));

            await _dbContext.OutboxMessages.InsertManyAsync(entities, cancellationToken: cancellationToken)
                                    .ConfigureAwait(false);
        }

        public ValueTask DeleteAsync(OutboxMessage message, string lockId, CancellationToken cancellationToken = default)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            if (string.IsNullOrWhiteSpace(lockId))
                throw new ArgumentException($"'{nameof(lockId)}' cannot be null or whitespace.", nameof(lockId));

            return DeleteAsyncCore(message, lockId, cancellationToken);
        }

        private async ValueTask DeleteAsyncCore(OutboxMessage message, string lockId, CancellationToken cancellationToken)
        {
            var filter = Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, message.MessageId);

            var entity = await _dbContext.OutboxMessages.FindOneAsync(filter, cancellationToken)
                                                        .ConfigureAwait(false);
            if (entity is null)
                throw new ArgumentException($"message '{message.MessageId}' not found");

            if (string.IsNullOrWhiteSpace(entity.LockId))
                throw new LockException($"message '{message.MessageId}' is not locked");

            if (entity.LockId != lockId)
                throw new LockException($"invalid lock id '{lockId}' on message '{message.MessageId}'");

            await _dbContext.OutboxMessages.DeleteOneAsync(filter, cancellationToken)
                                           .ConfigureAwait(false);
        }

        public ValueTask<string> LockAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return LockAsyncCore(message, cancellationToken);
        }

        private async ValueTask<string> LockAsyncCore(OutboxMessage message, CancellationToken cancellationToken)
        {
            var lockId = Guid.NewGuid().ToString();

            var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.MessageId, message.MessageId),
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

            var lockedMessage = await _dbContext.OutboxMessages
                .FindOneAndUpdateAsync(filter, update, options, cancellationToken)
                .ConfigureAwait(false);
            if (null == lockedMessage)
                throw new LockException($"message '{message.MessageId}' is already locked");

            return lockId;
        }

        public async ValueTask<IEnumerable<OutboxMessage>> ReadPendingAsync(CancellationToken cancellationToken = default)
        {
            var maxLockDate = DateTimeOffset.UtcNow - _options.LockMaxDuration;

            var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
            var filter = filterBuilder.Or(
                            filterBuilder.Eq(e => e.LockId, null),
                            filterBuilder.Lt(e => e.LockTime, maxLockDate)
                        );
            var cursor = await _dbContext.OutboxMessages.FindAsync(filter, cancellationToken: cancellationToken)
                                                         .ConfigureAwait(false);
            var entities = await cursor.ToListAsync(cancellationToken)
                                       .ConfigureAwait(false);
            if (entities is null)
                return Enumerable.Empty<OutboxMessage>();

            var messages = entities.Select(e => e.ToModel(_typeResolver))
                                   .Where(m => m is not null)
                                   .ToArray();

            return messages;
        }
    }
}