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

    // only the message as this repository last read it is deleted: one the transport returned is put back without
    // its claim, possibly before the relay that published it deletes it, and one that is gone is not an error.
    // A repository that never read deletes a message nobody has claimed
    private async ValueTask DeleteAsyncCore(MessageEnvelope message, CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(MessageIdField, message.MessageId),
            filterBuilder.Eq(LockIdField, _claimId));

        await _dbContext.OutboxMessages.DeleteOneAsync(filter, cancellationToken)
                                       .ConfigureAwait(false);
    }

    // built once: this delete runs for every message that is published
    private static readonly ExpressionFieldDefinition<Entities.OutboxMessage, string> MessageIdField = new(e => e.MessageId);
    private static readonly ExpressionFieldDefinition<Entities.OutboxMessage, string?> LockIdField = new(e => e.LockId);

    // the claim of the last read of this repository that claimed anything. The relay creates a repository for each pass
    // and reads once: a repository that is read again before it has deleted what it read first leaves those messages
    // to be published again when their claim expires
    private string? _claimId;

    /// <summary>
    /// stores the message again with its new due time, in place when the relay has not deleted it yet,
    /// and lets go of the claim it finds on it, which is the one of the relay that published it.
    /// </summary>
    /// <remarks>
    /// <para>Unless that claim expired while its relay was still publishing, and another relay claimed the message
    /// and published it as well: its claim is the one that is removed, its delete then removes nothing,
    /// and the message is published once more than the two relays already did.</para>
    /// <para>When two hosts store a message that is not there at the same time, one of them fails with a duplicate key,
    /// and succeeds when it tries again.</para>
    /// </remarks>
    public async ValueTask PutBackAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var entity = Entities.OutboxMessage.Create(message, _serializer);
        var filter = Builders<Entities.OutboxMessage>.Filter;
        var update = Builders<Entities.OutboxMessage>.Update;
        var sameMessage = filter.Eq(e => e.MessageId, entity.MessageId);

        // a time of first return that is earlier than the creation of the message is not one to keep.
        // On its own: an update cannot compare two fields of the document it changes
        await _dbContext.OutboxMessages.UpdateOneAsync(
            filter.And(sameMessage, filter.Lt(e => e.FirstReturnedAt, entity.CreatedAt.UtcDateTime)),
            update.Unset(e => e.FirstReturnedAt),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // the time of first return that is kept is the earliest, whoever stores it
        await _dbContext.OutboxMessages.UpdateOneAsync(
            sameMessage,
            update.Set(e => e.DeliverAt, entity.DeliverAt)
                  .Min(e => e.FirstReturnedAt, entity.FirstReturnedAt)
                  .Unset(e => e.LockId)
                  .Unset(e => e.LockTime)
                  .SetOnInsert(e => e.Body, entity.Body)
                  .SetOnInsert(e => e.CorrelationId, entity.CorrelationId)
                  .SetOnInsert(e => e.MessageType, entity.MessageType)
                  .SetOnInsert(e => e.CreatedAt, entity.CreatedAt)
                  .SetOnInsert(e => e.SenderId, entity.SenderId),
            new UpdateOptions { IsUpsert = true },
            cancellationToken).ConfigureAwait(false);
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

        _claimId = claimId;

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