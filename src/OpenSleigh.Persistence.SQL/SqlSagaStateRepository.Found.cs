using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQL;

// A message is handled by one repository: it finds the saga, locks it and releases it.
// What the find read is kept, so that the lock and the release do not ask the database for it again.
public partial class SqlSagaStateRepository
{
    private readonly Dictionary<string, FoundSaga> _foundSagas = new();

    private sealed record FoundSaga(SagaState? Row, HashSet<string> StoredMessageIds);

    private void RememberFoundSaga(SagaState row)
        => _foundSagas[row.InstanceId] = new FoundSaga(
            row,
            new HashSet<string>(row.ProcessedMessages.Select(message => message.MessageId)));

    private void RememberNewSaga(string instanceId)
        => _foundSagas[instanceId] = new FoundSaga(Row: null, new HashSet<string>());

    private void RememberStoredMessages(string instanceId, IReadOnlyCollection<string> messageIds)
    {
        if (_foundSagas.TryGetValue(instanceId, out var found))
            found.StoredMessageIds.UnionWith(messageIds);
    }

    /// <summary>
    /// locks the saga with one conditional update, using the row that <see cref="FindAsync"/> read.
    /// Returns null when the row has to be read again: it was not found by this repository,
    /// it looked locked when it was found, or its lock changed in the meantime.
    /// </summary>
    private async ValueTask<string?> TryLockFoundSagaAsync(ISagaInstance state, CancellationToken cancellationToken)
    {
        if (!_foundSagas.TryGetValue(state.InstanceId, out var found) || found.Row is null)
            return null;

        if (IsLocked(found.Row.LockId, found.Row.LockTime))
            return null;

        if (_dbContext.ChangeTracker.Entries<SagaState>().Any(entry => entry.Entity.InstanceId == state.InstanceId))
            return null;

        // LockId is a concurrency token: the update only applies while the lock is the one that was found
        var entity = CopyWithoutProcessedMessages(found.Row);
        _dbContext.SagaStates.Attach(entity);
        SetNewLock(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.Entry(entity).State = EntityState.Detached;
            return null;
        }
        catch
        {
            _dbContext.Entry(entity).State = EntityState.Detached;
            throw;
        }

        return entity.LockId;
    }

    private static SagaState CopyWithoutProcessedMessages(SagaState row)
        => new()
        {
            CorrelationId = row.CorrelationId,
            InstanceId = row.InstanceId,
            TriggerMessageId = row.TriggerMessageId,
            SagaType = row.SagaType,
            SagaStateType = row.SagaStateType,
            StateData = row.StateData,
            IsCompleted = row.IsCompleted,
            LockId = row.LockId,
            LockTime = row.LockTime,
        };
}
