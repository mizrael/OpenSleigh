using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh;

public interface ISagaInstance 
{
    /// <summary>
    /// id of the first message that started the saga execution.
    /// </summary>
    string TriggerMessageId { get; }

    /// <summary>
    /// correlation id across the messages.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// the current saga instance id.
    /// </summary>
    string InstanceId { get; }

    /// <summary>
    /// the types descriptor.
    /// </summary>
    SagaDescriptor Descriptor { get; }

    string LockId { get; }

    IReadOnlyCollection<ProcessedMessage> ProcessedMessages { get; }

    IReadOnlyCollection<MessageEnvelope> Outbox { get; }

    /// <summary>
    /// true if the execution is completed.
    /// </summary>
    bool IsCompleted { get; } //TODO: this might become an enum

    //TODO: this should be private
    void SetAsProcessed<TM>(IMessageContext<TM> messageContext) where TM : IMessage;

    //TODO: do we really need this?
    void MarkAsCompleted();

    bool CanProcess<TM>(IMessageContext<TM> messageContext) where TM : IMessage;

    ValueTask LockAsync(
        ISagaStateRepository sagaStateRepository,
        CancellationToken cancellationToken);

    ValueTask ProcessAsync<TM>(
        IMessageHandlerManager messageHandlerManager, 
        IMessageContext<TM> messageContext,
        ISagaExecutionService sagaExecutionService, 
        CancellationToken cancellationToken) where TM : IMessage;

    void Publish(MessageEnvelope message);
    void ClearOutbox();

    /// <summary>
    /// the record of the failure of the message being processed, stored when the instance is committed, released or abandoned.
    /// Null when the handler did not fail.
    /// </summary>
    SagaMessageFailure? Failure { get; }

    /// <param name="failure">the record to store with the instance, or null to store none.</param>
    void RecordFailure(SagaMessageFailure? failure);

    /// <summary>
    /// hands the lock this instance holds over to <paramref name="other"/>: the same saga, read again from its
    /// repository while this instance held the lock. This instance no longer holds it.
    /// </summary>
    void HandLockTo(ISagaInstance other);
}

public interface ISagaInstance<TS> : ISagaInstance 
{
    /// <summary>
    /// the custom saga state.
    /// </summary>
    TS State { get; }
}