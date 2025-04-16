using OpenSleigh.Transport;

namespace OpenSleigh;

public interface ISagaStateRepository
{
    ValueTask<ISagaExecutionContext?> FindAsync<TM>(
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
        ISagaExecutionContext state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// releases the saga instance from the lock.
    /// </summary>
    /// <param name="state"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask ReleaseAsync(
        ISagaExecutionContext state,
        CancellationToken cancellationToken = default);
}