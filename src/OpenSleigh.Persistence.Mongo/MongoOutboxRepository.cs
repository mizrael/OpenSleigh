using MongoDB.Driver;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.Mongo;

public record MongoOutboxRepositoryOptions(TimeSpan LockMaxDuration)
{
    public static readonly MongoOutboxRepositoryOptions Default = new(TimeSpan.FromMinutes(1));
}

public class MongoOutboxRepository : IOutboxRepository, IOutboxReturnRepository
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

    // a message the transport returned is put back with a new due time, possibly before the relay that published it
    // deletes it: only the message as the relay read it is deleted, and one that is gone is not an error
    private async ValueTask DeleteAsyncCore(MessageEnvelope message, CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(e => e.MessageId, message.MessageId),
            filterBuilder.Eq(e => e.DeliverAt, AsStored(message.DeliverAt)));

        // looked up by its id, as before there was a second condition: left to choose between the index on the id
        // and the one on the due time, the server tries both
        await _dbContext.OutboxMessages.DeleteOneAsync(filter, DeleteByMessageId, cancellationToken)
                                       .ConfigureAwait(false);
    }

    private static readonly DeleteOptions DeleteByMessageId = new()
    {
        Hint = new MongoDB.Bson.BsonDocument(nameof(Entities.OutboxMessage.MessageId), 1)
    };

    // dates are stored to the millisecond
    private static DateTime? AsStored(DateTimeOffset? time)
    {
        if (time is null)
            return null;

        var utc = time.Value.UtcDateTime;
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMillisecond));
    }

    /// <summary>
    /// stores each message again with its new due time, in place when the relay has not deleted it yet,
    /// and lets go of the claim of the relay that published it.
    /// </summary>
    public ValueTask PutBackAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            return ValueTask.CompletedTask;

        return PutBackAsyncCore(messages, cancellationToken);
    }

    private async ValueTask PutBackAsyncCore(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var update = Builders<Entities.OutboxMessage>.Update;

        // a message that came back twice is stored as it came back last
        var writes = messages
            .GroupBy(message => message.MessageId)
            .Select(sameMessage => Entities.OutboxMessage.Create(sameMessage.Last(), _serializer))
            .Select(entity => new UpdateOneModel<Entities.OutboxMessage>(
                Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, entity.MessageId),
                update.Set(e => e.DeliverAt, entity.DeliverAt)
                      .Set(e => e.FirstReturnedAt, entity.FirstReturnedAt)
                      .Unset(e => e.LockId)
                      .Unset(e => e.LockTime)
                      .SetOnInsert(e => e.Body, entity.Body)
                      .SetOnInsert(e => e.CorrelationId, entity.CorrelationId)
                      .SetOnInsert(e => e.MessageType, entity.MessageType)
                      .SetOnInsert(e => e.CreatedAt, entity.CreatedAt)
                      .SetOnInsert(e => e.SenderId, entity.SenderId))
            {
                IsUpsert = true
            })
            .ToArray();

        await _dbContext.OutboxMessages.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken)
                                       .ConfigureAwait(false);
    }

    public async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var available = Available(now, _options.LockMaxDuration);

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

        var filter = ClaimedBy(claimId, now);
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

    /// <summary>
    /// the messages a relay reading at <paramref name="now"/> can claim: due, and not claimed or with an expired claim.
    /// </summary>
    internal static FilterDefinition<Entities.OutboxMessage> Available(DateTimeOffset now, TimeSpan lockMaxDuration)
    {
        var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
        var maxLockDate = now - lockMaxDuration;

        return filterBuilder.And(
            Due(now),
            filterBuilder.Or(
                filterBuilder.Eq(e => e.LockId, null),
                filterBuilder.Lt(e => e.LockTime, maxLockDate)));
    }

    // the claimed messages were due when they were claimed: saying so again lets the server look them up
    // among the due ones only, instead of going through every message that is waiting for its time
    internal static FilterDefinition<Entities.OutboxMessage> ClaimedBy(string claimId, DateTimeOffset now)
    {
        var filterBuilder = Builders<Entities.OutboxMessage>.Filter;

        return filterBuilder.And(
            Due(now),
            filterBuilder.Eq(e => e.LockId, claimId));
    }

    // "not later than now" rather than "null or up to now": one condition on the field, that also holds for a message
    // stored without a due time, and that the server answers with one range of the index on it
    internal static FilterDefinition<Entities.OutboxMessage> Due(DateTimeOffset now)
    {
        var filterBuilder = Builders<Entities.OutboxMessage>.Filter;

        return filterBuilder.Not(filterBuilder.Gt(e => e.DeliverAt, now.UtcDateTime));
    }
}