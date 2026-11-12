using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQL;

public partial class SqlSagaStateRepository : ISagaStateCommitter
{
    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return ReleaseAsyncCore(state, Array.Empty<MessageEnvelope>(), canRetry: true, cancellationToken);
    }

    /// <summary>
    /// stores the messages the saga published and releases it, in one transaction.
    /// </summary>
    public ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return ReleaseAsyncCore(state, state.Outbox, canRetry: true, cancellationToken);
    }

    private async ValueTask ReleaseAsyncCore(
        ISagaInstance state,
        IReadOnlyCollection<MessageEnvelope> outbox,
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
            throw LockLost(state);

        OutboxMessage[] outboxEntities = [];
        try
        {
            await AddNewProcessedMessagesAsync(entity, state, cancellationToken).ConfigureAwait(false);

            entity.IsCompleted = state.IsCompleted;
            if (state.Descriptor.SagaStateType is not null && state.GetType().IsGenericType)
            {
                var extractor = _stateExtractors.GetOrAdd(state.Descriptor.SagaStateType, static t =>
                    (ISagaStateExtractor)Activator.CreateInstance(
                        typeof(SagaStateExtractor<>).MakeGenericType(t))!);
                entity.StateData = extractor.Extract(state, _serializer);
            }

            entity.LockTime = null;
            entity.LockId = null;

            outboxEntities = outbox.Select(message => OutboxMessage.Map(message, _serializer)).ToArray();
            _dbContext.OutboxMessages.AddRange(outboxEntities);

            // one save, one transaction: state, processed messages and outbox are stored together or not at all
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            ForgetUnsavedChanges();
            throw LockLost(state);
        }
        catch (DbUpdateException) when (canRetry)
        {
            ForgetUnsavedChanges();

            // a lock that was taken over can also surface as a key violation,
            // when the new owner has already stored the same processed message
            await ThrowIfLockLostAsync(state, cancellationToken).ConfigureAwait(false);

            // a message with an idempotency key that is already in the outbox is not stored twice
            var notStoredYet = await ExcludeStoredMessagesAsync(outbox, cancellationToken).ConfigureAwait(false);
            if (notStoredYet.Count == outbox.Count)
                throw;

            await ReleaseAsyncCore(state, notStoredYet, canRetry: false, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch
        {
            ForgetUnsavedChanges();
            throw;
        }

        StopTracking(outboxEntities);
    }

    // after a failed save the context would keep the saga as released and the new rows as pending:
    // a later release through this repository would find a lock that looks gone and leave the saga locked.
    // Forgetting everything makes the next call read the saga from the database
    private void ForgetUnsavedChanges()
        => _dbContext.ChangeTracker.Clear();

    private void StopTracking(IEnumerable<OutboxMessage> outboxEntities)
    {
        foreach (var outboxEntity in outboxEntities)
            _dbContext.Entry(outboxEntity).State = EntityState.Detached;
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

    // processed messages are only ever added: the ones already stored are left alone
    // instead of being deleted and written again on every release
    private async ValueTask AddNewProcessedMessagesAsync(SagaState entity, ISagaInstance state, CancellationToken cancellationToken)
    {
        if (state.ProcessedMessages.Count == 0)
            return;

        var storedMessageIds = await _dbContext.Set<SagaProcessedMessage>()
            .Where(m => m.InstanceId == state.InstanceId)
            .Select(m => m.MessageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var knownMessageIds = new HashSet<string>(storedMessageIds);

        foreach (var msg in state.ProcessedMessages)
        {
            if (!knownMessageIds.Add(msg.MessageId))
                continue;

            entity.ProcessedMessages.Add(new SagaProcessedMessage()
            {
                InstanceId = state.InstanceId,
                MessageId = msg.MessageId,
                When = msg.When,
                SagaState = entity
            });
        }
    }

    private static LockException LockLost(ISagaInstance state)
        => new($"unable to release Saga State '{state.InstanceId}' with lock id '{state.LockId}'");
}
