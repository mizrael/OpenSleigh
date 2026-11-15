using OpenSleigh.Transport;

namespace OpenSleigh;

public interface ISagaStateRepository
{
    ValueTask<ISagaInstance ?> FindAsync<TM>(
        SagaDescriptor descriptor, 
        IMessageContext<TM> messageContext, 
        CancellationToken cancellationToken = default)
        where TM : IMessage;

    /// <summary>
    /// locks the saga instance for processing.
    /// </summary>
    /// <typeparam name="TM"></typeparam>
    /// <param name="state"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask<string> LockAsync(
        ISagaInstance  state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// stores the saga instance (state, completed flag and processed messages) and releases it from the lock.
    /// </summary>
    /// <exception cref="LockException">the lock is no longer held by the instance: nothing was stored.</exception>
    /// <exception cref="OptimisticLockException">a message the instance processed was stored by someone else in the meantime:
    /// nothing was stored, and the lock is still held by the instance, which has to be abandoned.</exception>
    /// <param name="state"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask ReleaseAsync(
        ISagaInstance  state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// releases the saga instance from the lock and stores nothing of it: the state, the completed flag
    /// and the processed messages stay as they were stored before the instance was locked.
    /// A saga that this lock created, and that was never released, is removed: its first message was not handled,
    /// and a message that cannot start it does not find it.
    /// </summary>
    /// <exception cref="LockException">the lock is no longer held by the instance.</exception>
    ValueTask AbandonAsync(
        ISagaInstance state,
        CancellationToken cancellationToken = default);
}