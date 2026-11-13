using MongoDB.Driver;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.Mongo;

public record MongoOutboxRepositoryOptions(TimeSpan LockMaxDuration)
{
    public static readonly MongoOutboxRepositoryOptions Default = new(TimeSpan.FromMinutes(1));
}

public class MongoOutboxRepository : IOutboxRepository
{
    private readonly IDbContext _dbContext;
    private readonly MongoOutboxRepositoryOptions _options;
    private readonly ITypeResolver _typeResolver;
    private readonly ISerializer _serializer;

    public MongoOutboxRepository(
        IDbContext dbContext,
        MongoOutboxRepositoryOptions options,
        ITypeResolver typeResolver,
        ISerializer serializer)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return AppendAsyncCore(messages, cancellationToken);
    }

    private async ValueTask<OutboxAppendResult> AppendAsyncCore(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var entities = messages.Select(message => Entities.OutboxMessage.Create(message, _serializer));
       
        try
        {
            await _dbContext.OutboxMessages.InsertManyAsync(entities, cancellationToken: cancellationToken)
                                           .ConfigureAwait(false);
            return OutboxAppendResult.Success;
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors is not null && ex.WriteErrors.Any(w => w.Category == ServerErrorCategory.DuplicateKey))
        {
            return OutboxAppendResult.Duplicate;
        }        
    }

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return DeleteAsyncCore(message, cancellationToken);
    }

    private async ValueTask DeleteAsyncCore(MessageEnvelope message, CancellationToken cancellationToken)
    {
        var filter = Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, message.MessageId);

        var result = await _dbContext.OutboxMessages.DeleteOneAsync(filter, cancellationToken)
                                                    .ConfigureAwait(false);
        if (result.IsAcknowledged && result.DeletedCount == 0)
            throw new ArgumentException($"message '{message.MessageId}' not found");
    }

    public async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var maxLockDate = now - _options.LockMaxDuration;

        var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
        var available = filterBuilder.Or(
                        filterBuilder.Eq(e => e.LockId, null),
                        filterBuilder.Lt(e => e.LockTime, maxLockDate)
                    );

        // the messages are claimed before they are read, so that another relay reading at the same time
        // does not publish them too. A claim that is not followed by a delete expires after LockMaxDuration
        var claimId = Guid.NewGuid().ToString();
        var claim = Builders<Entities.OutboxMessage>.Update
            .Set(e => e.LockId, claimId)
            .Set(e => e.LockTime, now);
        var claimed = await _dbContext.OutboxMessages.UpdateManyAsync(available, claim, cancellationToken: cancellationToken)
                                                     .ConfigureAwait(false);
        if (claimed.IsAcknowledged && claimed.ModifiedCount == 0)
            return Enumerable.Empty<MessageEnvelope>();

        var filter = filterBuilder.Eq(e => e.LockId, claimId);
        var cursor = await _dbContext.OutboxMessages.FindAsync(filter, cancellationToken: cancellationToken)
                                                     .ConfigureAwait(false);
        var entities = await cursor.ToListAsync(cancellationToken)
                                   .ConfigureAwait(false);
        if (entities is null)
            return Enumerable.Empty<MessageEnvelope>();

        var results = new List<MessageEnvelope>(entities.Count);
        foreach(var entity in entities)
        {
            if (entity.TryMap(_typeResolver, _serializer, out var message))
                results.Add(message);
        }

        return results;
    }
}