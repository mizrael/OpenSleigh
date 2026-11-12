namespace OpenSleigh;

/// <summary>
/// implemented by a saga state repository that can store the messages a saga published together with its release,
/// so that none of them can be relayed, and come back to the saga, before the saga is released.
/// When the repository does not implement it, the messages are appended to the outbox first and the saga is released after.
/// </summary>
public interface ISagaStateCommitter
{
    /// <summary>
    /// stores the messages in the outbox of the saga instance and releases its lock.
    /// When the lock is no longer held, nothing is stored and a <see cref="LockException"/> is thrown.
    /// </summary>
    ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default);
}
