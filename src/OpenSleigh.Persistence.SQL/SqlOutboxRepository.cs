using Microsoft.EntityFrameworkCore;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQL
{
    [ExcludeFromCodeCoverage]
    public record SqlOutboxRepositoryOptions(TimeSpan LockMaxDuration)
    {
        public static readonly SqlOutboxRepositoryOptions Default = new (TimeSpan.FromMinutes(1));
    }
    
    public class SqlOutboxRepository : IOutboxRepository
    {
        private readonly ISagaDbContext _dbContext;
        private readonly SqlOutboxRepositoryOptions _options;
        private readonly ITypeResolver _typeResolver;

        public SqlOutboxRepository(ISagaDbContext dbContext, ITypeResolver typeResolver, SqlOutboxRepositoryOptions options)
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

            _dbContext.OutboxMessages.AddRange(entities);
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
        }

        public async ValueTask<IEnumerable<OutboxMessage>> ReadPendingAsync(CancellationToken cancellationToken = default)
        {
            var maxLockDate = DateTime.UtcNow - _options.LockMaxDuration;
            var entities = await _dbContext.OutboxMessages.AsNoTracking()
                    .Where(e => e.LockId == null || e.LockTime > maxLockDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entities is null)
                return Enumerable.Empty<OutboxMessage>();

            var messages = new List<OutboxMessage>();
            foreach (var entity in entities)
            {
                var message = entity.ToModel(_typeResolver);
                messages.Add(message);
            }

            return messages;
        }

        public ValueTask<string> LockAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return LockAsyncCore(message, cancellationToken);
        }

        private async ValueTask<string> LockAsyncCore(OutboxMessage message, CancellationToken cancellationToken)
        {
            var expirationDate = DateTime.UtcNow - _options.LockMaxDuration;

            var entity = await _dbContext.OutboxMessages.FirstOrDefaultAsync(e =>
                        e.MessageId == message.MessageId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (entity is null)
                throw new ArgumentException($"message '{message.MessageId}' not found");

            if (entity.LockId is not null && entity.LockTime > DateTime.UtcNow - _options.LockMaxDuration)
                throw new LockException($"message '{message.MessageId}' is already locked");

            entity.LockId = Guid.NewGuid().ToString();
            entity.LockTime = DateTimeOffset.UtcNow;            

            await _dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            return entity.LockId;
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
                var entity = await _dbContext.OutboxMessages
                    .FirstOrDefaultAsync(e =>
                        e.MessageId == message.MessageId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (entity is null)
                    throw new ArgumentException($"message '{message.MessageId}' not found");

                if (string.IsNullOrWhiteSpace(entity.LockId))
                    throw new LockException($"message '{message.MessageId}' is not locked");

                if (entity.LockId != lockId)
                    throw new LockException($"invalid lock id '{lockId}' on message '{message.MessageId}'");

            _dbContext.OutboxMessages.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);             
        }
    }
}
