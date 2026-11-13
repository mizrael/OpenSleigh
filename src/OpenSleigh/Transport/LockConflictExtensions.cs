namespace OpenSleigh.Transport;

public static class LockConflictExtensions
{
    /// <summary>
    /// tells a transport what to do with a message that could not be processed.
    /// A lock conflict means another worker was processing the same saga instance: the message is fine and has to be
    /// delivered again after a delay. Any other failure is not going to go away by itself, and the message
    /// goes to the dead letter destination.
    /// </summary>
    /// <returns>true when the saga instance was locked, or changed, by someone else. An <see cref="AggregateException"/>
    /// counts when any of its inner exceptions does: that is what a failed second attempt to lock is reported as.</returns>
    public static bool IsLockConflict(this Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LockException or OptimisticLockException)
            return true;

        if (exception is not AggregateException aggregate)
            return false;

        foreach (var inner in aggregate.InnerExceptions)
            if (inner is LockException or OptimisticLockException)
                return true;

        return false;
    }
}
