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
    /// </summary>
    /// <exception cref="LockException">
    /// nothing was stored, and the saga is not locked by the instance any more: its lock was lost.
    /// The instance is not abandoned afterwards. After any other failure the lock is still held, and the instance is.
    /// </exception>
    ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default);
}
