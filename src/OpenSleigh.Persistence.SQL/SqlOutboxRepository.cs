using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Utils;

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
        private readonly ISerializer _serializer;
        private readonly SqlOutboxRepositoryOptions _options;

        private enum MessageStatuses
        {
            Pending,
            Processed
        }

        public SqlOutboxRepository(ISagaDbContext dbContext, ISerializer serializer, SqlOutboxRepositoryOptions options)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<IEnumerable<IMessage>> ReadMessagesToProcess(CancellationToken cancellationToken = default)
        {
            var maxLockDate = DateTime.UtcNow - _options.LockMaxDuration;
            var entities = await _dbContext.OutboxMessages.AsNoTracking()
                    .Where(e =>
                        e.Status == MessageStatuses.Pending.ToString() &&
                        (e.LockId == null || e.LockTime > maxLockDate))
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
            var transaction = await _dbContext.StartTransactionAsync(cancellationToken);
            try
            {
                var entity = await _dbContext.OutboxMessages
                    .FirstOrDefaultAsync(e => e.Id == message.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (entity is null)
                    throw new ArgumentException($"message '{message.Id}' not found");

                if (!entity.LockId.HasValue)
                    throw new LockException($"message '{message.Id}' is not locked");

                if (entity.LockId != lockId)
                    throw new LockException($"invalid lock id '{lockId}' on message '{message.Id}'");

                entity.LockId = null;
                entity.LockTime = null;
                entity.PublishingDate = DateTime.UtcNow;
                entity.Status = MessageStatuses.Processed.ToString();
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public Task AppendAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return AppendAsyncCore(message, cancellationToken);
        }

        private async Task AppendAsyncCore(IMessage message, CancellationToken cancellationToken)
        {
            var serialized = await _serializer.SerializeAsync(message, cancellationToken);
            var entity = new Entities.OutboxMessage(message.Id, serialized, message.GetType().FullName)
            {
                Status = MessageStatuses.Pending.ToString()
            };
            _dbContext.OutboxMessages.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task CleanProcessedAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await _dbContext.StartTransactionAsync(cancellationToken);
            try
            {
                var messages = await _dbContext.OutboxMessages
                    .Where(e => e.Status == MessageStatuses.Processed.ToString())
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                
                if (messages.Any())
                {
                    _dbContext.OutboxMessages.RemoveRange(messages);

                    await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public Task<Guid> LockAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return LockAsyncCore(message, cancellationToken);
        }
        
        private async Task<Guid> LockAsyncCore(IMessage message, CancellationToken cancellationToken)
        {
            var transaction = await _dbContext.StartTransactionAsync(cancellationToken);
            try
            {
                var entity = await _dbContext.OutboxMessages.FirstOrDefaultAsync(e => e.Id == message.Id,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (entity is null)
                    throw new ArgumentException($"message '{message.Id}' not found");

                if (entity.LockId.HasValue && entity.LockTime.Value > DateTime.UtcNow - _options.LockMaxDuration)
                    throw new LockException($"message '{message.Id}' is already locked");

                if (entity.Status == MessageStatuses.Processed.ToString())
                    throw new LockException($"message '{message.Id}' was already processed");

                entity.LockId = Guid.NewGuid();
                entity.LockTime = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken);

                return entity.LockId.Value;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
