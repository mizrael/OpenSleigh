using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL.Entities;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace OpenSleigh.Persistence.SQL;

// A message is handled by one repository: it finds the saga, locks it and releases it.
// What the find read is kept with the instance it returned, so that the lock and the release
// do not ask the database for it again, and so that a saga that changed since it was found is not locked as it was.
public partial class SqlSagaStateRepository
{
    // keyed by the instance itself: another message gets another instance, and nothing outlives its instance
    private readonly ConditionalWeakTable<ISagaInstance, KnownSaga> _knownSagas = new();

    // the saga this repository last looked for and did not find
    private string? _sagaNotFound;

    private sealed class KnownSaga
    {
        /// <summary>the row as <see cref="FindAsync"/> read it. Null once the saga is locked, or when it was not found.</summary>
        public SagaState? FoundRow { get; set; }

        /// <summary>ids of the processed messages that are stored. Null when they have to be read.</summary>
        public HashSet<string>? StoredMessageIds { get; init; }
    }

    private bool WasNotFound(string correlationId, string? sagaType, string? sagaStateType)
    {
        var wasNotFound = _sagaNotFound == SagaKey(correlationId, sagaType, sagaStateType);
        _sagaNotFound = null;
        return wasNotFound;
    }

    private static string SagaKey(string correlationId, string? sagaType, string? sagaStateType)
        => $"{correlationId}|{sagaType}|{sagaStateType}";

    private void RememberFoundSaga(ISagaInstance instance, SagaState row)
        => _knownSagas.AddOrUpdate(instance, new KnownSaga
        {
            FoundRow = row,
            StoredMessageIds = new HashSet<string>(row.ProcessedMessages.Select(message => message.MessageId))
        });

    private void RememberLockedSaga(ISagaInstance instance, bool isNew)
        => _knownSagas.AddOrUpdate(instance, new KnownSaga
        {
            StoredMessageIds = isNew ? new HashSet<string>() : null
        });

    private bool TryGetFoundRow(ISagaInstance instance, [NotNullWhen(true)] out SagaState? foundRow)
    {
        foundRow = _knownSagas.TryGetValue(instance, out var known) ? known.FoundRow : null;
        return foundRow is not null;
    }

    /// <summary>
    /// locks a saga that <see cref="FindAsync"/> returned, with one update that only applies while the saga
    /// is still as it was found: every lock and every release leaves another <see cref="SagaState.LockId"/> behind.
    /// </summary>
    /// <exception cref="LockException">someone else holds the lock.</exception>
    /// <exception cref="OptimisticLockException">the saga was locked and released by someone else since it was found:
    /// what was found is out of date, and the saga has to be looked for again.</exception>
    private async ValueTask<string> LockFoundSagaAsync(ISagaInstance state, SagaState foundRow, CancellationToken cancellationToken)
    {
        var lockedWhenFound = IsLocked(foundRow.LockId, foundRow.LockTime);
        if (!lockedWhenFound && FindTracked(state.InstanceId) is null)
        {
            var lockId = await TryLockAsFoundAsync(state, foundRow, cancellationToken).ConfigureAwait(false);
            if (lockId is not null)
                return lockId;
        }

        // the saga was locked when it was found, or is no longer as it was found: its lock is read, to tell which
        var current = await _dbContext.SagaStates
            .AsNoTracking()
            .Where(e => e.InstanceId == state.InstanceId)
            .Select(e => new { e.LockId, e.LockTime })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current is not null && IsLocked(current.LockId, current.LockTime))
            throw AlreadyLocked(state);

        if (current is null || current.LockId != foundRow.LockId)
        {
            _knownSagas.Remove(state);
            throw new OptimisticLockException($"saga state '{state.InstanceId}' has changed since it was found");
        }

        // still the lock that was found, and expired by now: its owner never released the saga, which is as it was found
        return await TryLockAsFoundAsync(state, foundRow, cancellationToken).ConfigureAwait(false)
            ?? throw AlreadyLocked(state);
    }

    // LockId is a concurrency token: the update only applies while it is the one that was found.
    // Returns null when it is not
    private async ValueTask<string?> TryLockAsFoundAsync(ISagaInstance state, SagaState foundRow, CancellationToken cancellationToken)
    {
        var entity = CopyWithoutProcessedMessages(foundRow);
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

        // the saga is locked: the row that was found is not what is stored any more
        if (_knownSagas.TryGetValue(state, out var known))
            known.FoundRow = null;

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
