using MongoDB.Driver;
using OpenSleigh.Outbox;

namespace OpenSleigh.Persistence.Mongo;

public partial class MongoSagaStateRepository : ISagaStateCommitter
{
    /// <summary>
    /// stores the messages the saga published and releases it.
    /// </summary>
    /// <remarks>
    /// Without a transaction over the two collections, the messages are stored first but held: the relay skips them,
    /// the same way it skips messages claimed by another relay. They are let go once the saga is released.
    /// If the process stops in between, the hold expires after the outbox lock duration and the messages are relayed then;
    /// if the release fails, they are removed.
    /// </remarks>
    public async ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Outbox.Count == 0)
        {
            await ReleaseAsync(state, cancellationToken).ConfigureAwait(false);
            return;
        }

        var holdId = Guid.NewGuid().ToString();
        await StoreHeldMessagesAsync(state.Outbox, holdId, cancellationToken).ConfigureAwait(false);
        var heldMessages = HeldBy(holdId, state.Outbox);

        try
        {
            await ReleaseAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryRemoveHeldMessagesAsync(heldMessages).ConfigureAwait(false);
            throw;
        }

        await TryLetGoOfHeldMessagesAsync(heldMessages).ConfigureAwait(false);
    }

    private async ValueTask StoreHeldMessagesAsync(IEnumerable<MessageEnvelope> outbox, string holdId, CancellationToken cancellationToken)
    {
        var heldSince = DateTimeOffset.UtcNow;
        var entities = outbox.Select(message =>
        {
            var entity = Entities.OutboxMessage.Create(message, _serializer);
            entity.LockId = holdId;
            entity.LockTime = heldSince;
            return entity;
        });

        try
        {
            // unordered: a message with an idempotency key that is already in the outbox does not stop the others
            await _dbContext.OutboxMessages
                .InsertManyAsync(entities, new InsertManyOptions { IsOrdered = false }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors.All(error => error.Category == ServerErrorCategory.DuplicateKey))
        {
        }
    }

    private async ValueTask TryRemoveHeldMessagesAsync(FilterDefinition<Entities.OutboxMessage> heldMessages)
    {
        try
        {
            await _dbContext.OutboxMessages.DeleteManyAsync(heldMessages).ConfigureAwait(false);
        }
        catch
        {
            // the saga was not released, which is the error to report. Whatever stays behind is relayed when its hold expires
        }
    }

    private async ValueTask TryLetGoOfHeldMessagesAsync(FilterDefinition<Entities.OutboxMessage> heldMessages)
    {
        var letGo = Builders<Entities.OutboxMessage>.Update
            .Set(e => e.LockId, null)
            .Set(e => e.LockTime, null);

        try
        {
            // not cancellable: the saga is released, and stopping here would only delay its messages
            await _dbContext.OutboxMessages.UpdateManyAsync(heldMessages, letGo).ConfigureAwait(false);
        }
        catch
        {
            // the saga is committed: failing now would have its message processed again for nothing.
            // The messages are relayed when their hold expires
        }
    }

    // looked up by message id, which is indexed: by hold alone, the server would go through the whole outbox.
    // The hold still tells the messages this commit stored from one that was already there under the same id
    internal static FilterDefinition<Entities.OutboxMessage> HeldBy(string holdId, IEnumerable<MessageEnvelope> outbox)
    {
        var filterBuilder = Builders<Entities.OutboxMessage>.Filter;
        var messageIds = outbox.Select(message => message.MessageId).Distinct().ToArray();

        return filterBuilder.And(
            filterBuilder.In(e => e.MessageId, messageIds),
            filterBuilder.Eq(e => e.LockId, holdId));
    }
}
