using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Threading;

namespace OpenSleigh
{
    public interface ISagaExecutionContext
    {
        /// <summary>
        /// id of the current message triggering the execution.
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

        IReadOnlyCollection<OutboxMessage> Outbox { get; }

        /// <summary>
        /// true if the execution is completed.
        /// </summary>
        bool IsCompleted { get; } //TODO: this might become an enum

        //TODO: this should be private
        void SetAsProcessed<TM>(IMessageContext<TM> messageContext) where TM : IMessage;

        //TODO: do we really need this?
        void MarkAsCompleted();

        bool CanProcess<TM>(IMessageContext<TM> messageContext) where TM : IMessage;

        ValueTask LockAsync(ISagaStateRepository sagaStateRepository, CancellationToken cancellationToken);
        ValueTask ProcessAsync<TM>(
            IMessageHandlerManager messageHandlerManager, 
            IMessageContext<TM> messageContext,
            ISagaExecutionService sagaExecutionService, 
            CancellationToken cancellationToken) where TM : IMessage;

        void Publish(OutboxMessage message);
        void ClearOutbox();        
    }

    public interface ISagaExecutionContext<TS> : ISagaExecutionContext
    {
        /// <summary>
        /// the custom saga state.
        /// </summary>
        TS State { get; }
    }
}