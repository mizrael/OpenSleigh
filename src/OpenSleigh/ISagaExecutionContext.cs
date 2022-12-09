using OpenSleigh.Messaging;

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

        /// <summary>
        /// true if the execution is completed.
        /// </summary>
        bool IsCompleted { get; } //TODO: this might become an enum

        void SetAsProcessed<TM>(IMessageContext<TM> messageContext) where TM : IMessage;
        void MarkAsCompleted();
        bool CanProcess<TM>(IMessageContext<TM> messageContext) where TM : IMessage;
    }

    public interface ISagaExecutionContext<TS> : ISagaExecutionContext
    {
        /// <summary>
        /// the custom saga state.
        /// </summary>
        TS State { get; }
    }
}