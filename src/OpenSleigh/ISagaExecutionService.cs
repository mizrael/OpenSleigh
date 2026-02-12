using OpenSleigh.Transport;

namespace OpenSleigh;

public interface ISagaExecutionService
{
    /// <summary>
    /// fetches the saga instance for the given message context, checks if it can process the message, and locks it for processing.
    /// </summary>
    /// <typeparam name="TM"></typeparam>
    /// <param name="messageContext"></param>
    /// <param name="descriptor"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>the saga instance for the given message context.</returns>
    ValueTask<ISagaInstance> BeginProcessingAsync<TM>(
        IMessageContext<TM> messageContext,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken = default) where TM : IMessage;

    /// <summary>
    /// commits the saga instance state and outbox messages.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask CommitAsync(
        ISagaInstance context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// releases the lock on the saga instance without persisting state changes.
    /// </summary>
    ValueTask ReleaseAsync(
        ISagaInstance context,
        CancellationToken cancellationToken = default);
}