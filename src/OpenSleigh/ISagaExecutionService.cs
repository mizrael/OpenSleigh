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
    /// releases the lock on the saga instance and stores nothing of it but the record of its failure, if it has one:
    /// the saga stays as it was before the message.
    /// </summary>
    ValueTask AbandonAsync(
        ISagaInstance context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// decides what happens to a message whose handler failed, and stores the decision.
    /// When the saga has a compensation handler for the message it runs, on the saga as it was before the failed handler,
    /// and the message counts as processed. Otherwise, or when the compensation fails too, the saga is left as it was
    /// and the failure is thrown, for the transport to dead-letter the message.
    /// </summary>
    /// <param name="context">the instance the handler failed on. It holds the lock on the saga.</param>
    /// <param name="failure">what the handler manager threw. Its inner exception is the one the handler threw.</param>
    ValueTask HandleFailureAsync<TM>(
        ISagaInstance context,
        IMessageContext<TM> messageContext,
        IMessageHandlerManager messageHandlerManager,
        SagaException failure,
        CancellationToken cancellationToken = default) where TM : IMessage;
}