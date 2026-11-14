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
    /// <param name="state"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask ReleaseAsync(
        ISagaInstance  state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// releases the saga instance from the lock and stores nothing of it: the state, the completed flag
    /// and the processed messages stay as they were stored before the instance was locked.
    /// </summary>
    ValueTask AbandonAsync(
        ISagaInstance state,
        CancellationToken cancellationToken = default);
}