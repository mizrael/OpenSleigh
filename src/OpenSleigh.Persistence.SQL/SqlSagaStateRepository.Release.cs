using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQL;

public partial class SqlSagaStateRepository : ISagaStateCommitter
{
    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return ReleaseAsyncCore(state, Array.Empty<MessageEnvelope>(), storeInstance: true, canRetry: true, cancellationToken);
    }

    /// <summary>
    /// releases the saga. The saga itself stays as it was stored.
    /// </summary>
    public ValueTask AbandonAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return ReleaseAsyncCore(state, Array.Empty<MessageEnvelope>(), storeInstance: false, canRetry: true, cancellationToken);
    }

    /// <summary>
    /// stores the messages the saga published and releases it, in one transaction.
    /// </summary>
    public ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return ReleaseAsyncCore(state, state.Outbox, storeInstance: true, canRetry: true, cancellationToken);
    }

    private async ValueTask ReleaseAsyncCore(
        ISagaInstance state,
        IReadOnlyCollection<MessageEnvelope> outbox,
        bool storeInstance,
        bool canRetry,
        CancellationToken cancellationToken)
    {
        // the saga was loaded by this context when it was locked: it is not read from the database again.
        // LockId is a concurrency token, so releasing a lock that someone else took over still fails on save
        var entity = await _dbContext.SagaStates
             .FindAsync([state.InstanceId], cancellationToken)
             .ConfigureAwait(false);

        if (entity is null)
            throw new ArgumentException($"saga state '{state.InstanceId}' not found");

        if (entity.LockId != state.LockId)
        {
            ForgetUnsavedChanges(state);
            throw LockLost(state);
        }

        IReadOnlyCollection<string> newMessageIds = [];
        try
        {
            if (storeInstance)
                newMessageIds = await StoreInstanceAsync(entity, state, cancellationToken).ConfigureAwait(false);

            // the saga is unlocked by its lock time. Its lock id is replaced, not removed:
            // whoever found the saga before this release cannot lock it as it was then
            entity.LockTime = null;
            entity.LockId = NewLockId();

            _dbContext.OutboxMessages.AddRange(outbox.Select(message => OutboxMessage.Map(message, _serializer)));

            // one save, one transaction: state, processed messages and outbox are stored together or not at all
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            ForgetUnsavedChanges(state);
            throw LockLost(state);
        }
        catch (DbUpdateException) when (canRetry)
        {
            ForgetUnsavedChanges(state);

            // a lock that was taken over can also surface as a key violation,
            // when the new owner has already stored the same processed message
            await ThrowIfLockLostAsync(state, cancellationToken).ConfigureAwait(false);

            // and so does a message that someone else handled and stored while this saga held what it found:
            // their state is the one that is stored, and nothing of this one is
            await ThrowIfAlreadyProcessedAsync(state, newMessageIds, cancellationToken).ConfigureAwait(false);

            // a message with an idempotency key that is already in the outbox is not stored twice
            var notStoredYet = await ExcludeStoredMessagesAsync(outbox, cancellationToken).ConfigureAwait(false);
            if (notStoredYet.Count == outbox.Count)
                throw;

            await ReleaseAsyncCore(state, notStoredYet, storeInstance, canRetry: false, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch
        {
            ForgetUnsavedChanges(state);
            throw;
        }

        // the saga is released: what this repository knows about it is of no use to whoever locks it next
        ForgetUnsavedChanges(state);
    }

    // after a failed save the context would keep the saga as released and the new rows as pending:
    // a later release through this repository would find a lock that looks gone and leave the saga locked.
    // After a release it would keep the saga as it was then, and a later lock would start from that.
    // Forgetting everything makes the next call read the saga from the database
    private void ForgetUnsavedChanges(ISagaInstance state)
    {
        _dbContext.ChangeTracker.Clear();
        _knownSagas.Remove(state);
    }

    private async ValueTask ThrowIfLockLostAsync(ISagaInstance state, CancellationToken cancellationToken)
    {
        var storedLockId = await _dbContext.SagaStates
            .AsNoTracking()
            .Where(e => e.InstanceId == state.InstanceId)
            .Select(e => e.LockId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (storedLockId != state.LockId)
            throw LockLost(state);
    }

    private async ValueTask ThrowIfAlreadyProcessedAsync(ISagaInstance state, IReadOnlyCollection<string> newMessageIds, CancellationToken cancellationToken)
    {
        if (newMessageIds.Count == 0)
            return;

        var storedMessageIds = await ReadStoredMessageIdsAsync(state.InstanceId, cancellationToken).ConfigureAwait(false);
        if (storedMessageIds.Overlaps(newMessageIds))
            throw new LockException(
                $"unable to release Saga State '{state.InstanceId}': a message it processed was processed and stored by someone else");
    }

    private async ValueTask<IReadOnlyCollection<MessageEnvelope>> ExcludeStoredMessagesAsync(
        IReadOnlyCollection<MessageEnvelope> outbox,
        CancellationToken cancellationToken)
    {
        if (outbox.Count == 0)
            return outbox;

        var messageIds = outbox.Select(message => message.MessageId).ToArray();
        var storedIds = await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(e => messageIds.Contains(e.MessageId))
            .Select(e => e.MessageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return storedIds.Count == 0
            ? outbox
            : outbox.Where(message => !storedIds.Contains(message.MessageId)).ToArray();
    }

    private async ValueTask<IReadOnlyCollection<string>> StoreInstanceAsync(SagaState entity, ISagaInstance state, CancellationToken cancellationToken)
    {
        var newMessageIds = await AddNewProcessedMessagesAsync(entity, state, cancellationToken).ConfigureAwait(false);

        entity.IsCompleted = state.IsCompleted;

        var stateData = ExtractStateData(state);
        if (stateData is not null)
            entity.StateData = stateData;

        return newMessageIds;
    }

    // processed messages are only ever added: the ones already stored are left alone
    // instead of being deleted and written again on every release.
    // Which ones are stored is known when this repository found the saga, and read from the database otherwise
    private async ValueTask<IReadOnlyCollection<string>> AddNewProcessedMessagesAsync(SagaState entity, ISagaInstance state, CancellationToken cancellationToken)
    {
        if (state.ProcessedMessages.Count == 0)
            return [];

        var storedMessageIds = _knownSagas.TryGetValue(state, out var known) && known.StoredMessageIds is not null
            ? known.StoredMessageIds
            : await ReadStoredMessageIdsAsync(state.InstanceId, cancellationToken).ConfigureAwait(false);

        var newMessageIds = new List<string>();

        foreach (var msg in state.ProcessedMessages)
        {
            if (storedMessageIds.Contains(msg.MessageId))
                continue;

            newMessageIds.Add(msg.MessageId);
            entity.ProcessedMessages.Add(new SagaProcessedMessage()
            {
                InstanceId = state.InstanceId,
                MessageId = msg.MessageId,
                When = msg.When,
                SagaState = entity
            });
        }

        return newMessageIds;
    }

    private async ValueTask<HashSet<string>> ReadStoredMessageIdsAsync(string instanceId, CancellationToken cancellationToken)
    {
        var storedMessageIds = await _dbContext.Set<SagaProcessedMessage>()
            .AsNoTracking()
            .Where(m => m.InstanceId == instanceId)
            .Select(m => m.MessageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<string>(storedMessageIds);
    }

    private static LockException LockLost(ISagaInstance state)
        => new($"unable to release Saga State '{state.InstanceId}' with lock id '{state.LockId}'");
}
