using OpenSleigh.Messaging;

namespace OpenSleigh
{
    public record SagaExecutionContext : ISagaExecutionContext
    {
        private HashSet<string> _processedMessages = new();

        public SagaExecutionContext(
            string instanceId, 
            string triggerMessageId, 
            string correlationId,
            SagaDescriptor descriptor,
            IEnumerable<string>? processedMessagesIds = null)
        {
            if (string.IsNullOrWhiteSpace(instanceId))            
                throw new ArgumentException($"'{nameof(instanceId)}' cannot be null or whitespace.", nameof(instanceId));
            
            if (string.IsNullOrWhiteSpace(triggerMessageId))            
                throw new ArgumentException($"'{nameof(triggerMessageId)}' cannot be null or whitespace.", nameof(triggerMessageId));

            if (string.IsNullOrWhiteSpace(correlationId))
                throw new ArgumentException($"'{nameof(correlationId)}' cannot be null or whitespace.", nameof(correlationId));

            InstanceId = instanceId;
            TriggerMessageId = triggerMessageId;
            CorrelationId = correlationId;
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));      
            
            if(processedMessagesIds is not null)
                foreach(var id in processedMessagesIds)
                    _processedMessages.Add(id);
        }

        public bool CanProcess<TM>(IMessageContext<TM> messageContext) 
            where TM : IMessage
        {
            if (IsCompleted)
                return false;

            if (this.CorrelationId != messageContext.CorrelationId)
                return false;

            if (_processedMessages.Contains(messageContext.Id))
                return false;

            var messageType = messageContext.Message.GetType();
            var isInitiator = this.Descriptor.InitiatorType == messageType;
            if (isInitiator)
                return true;            
            
            return true;
        }
           
        public void SetAsProcessed<TM>(IMessageContext<TM> messageContext) where TM : IMessage
            => _processedMessages.Add(messageContext.Id);

        public void MarkAsCompleted()
            => this.IsCompleted = true;

        public string TriggerMessageId { get; }
        
        public string CorrelationId { get; }

        public string InstanceId { get; }

        public SagaDescriptor Descriptor { get; }

        public bool IsCompleted { get; private set; }
    }

    public record SagaExecutionContext<TS> : SagaExecutionContext, ISagaExecutionContext<TS>
    {
        public SagaExecutionContext(
            string instanceId, 
            string triggerMessageId, 
            string correlationId,
            SagaDescriptor descriptor, 
            TS state,
            IEnumerable<string>? processedMessagesIds = null) : base(
                instanceId: instanceId, 
                triggerMessageId: triggerMessageId,
                correlationId: correlationId,
                descriptor: descriptor,
                processedMessagesIds: processedMessagesIds)
        {
            this.State = state;
        }

        public TS State { get; }    
    }
}