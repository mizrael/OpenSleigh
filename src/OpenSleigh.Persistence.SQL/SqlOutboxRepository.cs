using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public record SqlOutboxRepositoryOptions(TimeSpan LockMaxDuration)
{
    public static readonly SqlOutboxRepositoryOptions Default = new (TimeSpan.FromMinutes(1));
}

public delegate bool DuplicateKeyDetector(Exception exception);


public class SqlOutboxRepository : IOutboxRepository
{
    private readonly SagaDbContext _dbContext;
    private readonly SqlOutboxRepositoryOptions _options;
    private readonly ITypeResolver _typeResolver;
    private readonly ISerializer _serializer;
    private readonly DuplicateKeyDetector _duplicateKeyDetector;

    public SqlOutboxRepository(
        SagaDbContext dbContext,
        ITypeResolver typeResolver,
        SqlOutboxRepositoryOptions options,
        ISerializer serializer,
        DuplicateKeyDetector duplicateKeyDetector)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _duplicateKeyDetector = duplicateKeyDetector;
    }

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return AppendAsyncCore(messages, cancellationToken);
    }

    private async ValueTask<OutboxAppendResult> AppendAsyncCore(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var entities = messages.Select(message => Entities.OutboxMessage.Map(message, _serializer));

        try
        {
            _dbContext.OutboxMessages.AddRange(entities);
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
            return OutboxAppendResult.Success;
        }
        catch (Exception ex) when (_duplicateKeyDetector(ex))
        {
            return OutboxAppendResult.Duplicate;
        }
    }

    public async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var maxLockDate = DateTimeOffset.UtcNow - _options.LockMaxDuration;
        var entities = await _dbContext.OutboxMessages.AsNoTracking()
                .Where(e => e.LockId == null || e.LockTime > maxLockDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        if (entities is null)
            return Array.Empty<MessageEnvelope>();

        var messages = new List<MessageEnvelope>(entities.Count);
        foreach (var entity in entities)
        {
            if (entity.TryMap(_typeResolver, _serializer, out var m))
                messages.Add(m);    
        }
        return messages;
    }

    public ValueTask<string> LockAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return LockAsyncCore(message, cancellationToken);
    }

    private async ValueTask<string> LockAsyncCore(MessageEnvelope message, CancellationToken cancellationToken)
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

        entity.LockId = Guid.CreateVersion7().ToString("N");
        entity.LockTime = DateTimeOffset.UtcNow;            

        await _dbContext.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity.LockId;
    }

    public ValueTask DeleteAsync(MessageEnvelope message, string lockId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(lockId))           
            throw new ArgumentException($"'{nameof(lockId)}' cannot be null or whitespace.", nameof(lockId));
        
        return DeleteAsyncCore(message, lockId, cancellationToken);
    }

    private async ValueTask DeleteAsyncCore(MessageEnvelope message, string lockId, CancellationToken cancellationToken)
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
