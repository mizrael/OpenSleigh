using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh
{
    public record SagaExecutionContext : ISagaExecutionContext
    {
        private readonly HashSet<ProcessedMessage> _processedMessages = new();
        private readonly ConcurrentQueue<OutboxMessage> _outbox = new();

        public SagaExecutionContext(
            string instanceId, 
            string triggerMessageId, 
            string correlationId,
            SagaDescriptor descriptor,
            IEnumerable<ProcessedMessage>? processedMessages = null)
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
            
            if(processedMessages is not null)
                foreach(var msg in processedMessages)
                    _processedMessages.Add(msg);
        }

        public bool CanProcess<TM>(IMessageContext<TM> messageContext) 
            where TM : IMessage
        {
            if (IsCompleted)
                return false;

            if (this.CorrelationId != messageContext.CorrelationId)
                return false;

            //TODO: need to speed up this one
            if (_processedMessages.Any(m => m.MessageId == messageContext.Id))
                return false;

            var messageType = messageContext.Message.GetType();
            var isInitiator = this.Descriptor.InitiatorType == messageType;
            if (isInitiator)
                return true;            
            
            return true;
        }
           
        public void SetAsProcessed<TM>(IMessageContext<TM> messageContext) where TM : IMessage
            => _processedMessages.Add(ProcessedMessage.Create(messageContext));

        public void MarkAsCompleted()
            => this.IsCompleted = true;

        public async ValueTask LockAsync(ISagaStateRepository sagaStateRepository, CancellationToken cancellationToken)
        {
            this.LockId = await sagaStateRepository.LockAsync(this, cancellationToken)
                                                  .ConfigureAwait(false);
        }

        public void Publish(OutboxMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            _outbox.Enqueue(message);
        }

        public void ClearOutbox()
            => _outbox.Clear();

        public async ValueTask ProcessAsync<TM>(
            IMessageHandlerManager messageHandlerManager, 
            IMessageContext<TM> messageContext,
            ISagaExecutionService sagaExecutionService,
            CancellationToken cancellationToken) where TM : IMessage
        {
            await messageHandlerManager.ProcessAsync(messageContext, this, cancellationToken)
                                       .ConfigureAwait(false);

            this.SetAsProcessed(messageContext);

            await sagaExecutionService.CommitAsync(this, cancellationToken)
                                      .ConfigureAwait(false);

            this.LockId = string.Empty;
        }

        public string TriggerMessageId { get; }
        
        public string CorrelationId { get; }

        public string InstanceId { get; }

        public SagaDescriptor Descriptor { get; }

        public bool IsCompleted { get; private set; }

        public string LockId { get; private set; }

        public IReadOnlyCollection<ProcessedMessage> ProcessedMessages => _processedMessages;
        public IReadOnlyCollection<OutboxMessage> Outbox => _outbox;
    }

    public record SagaExecutionContext<TS> : SagaExecutionContext, ISagaExecutionContext<TS>
    {
        public SagaExecutionContext(
            string instanceId, 
            string triggerMessageId, 
            string correlationId,
            SagaDescriptor descriptor, 
            TS state,
            IEnumerable<ProcessedMessage>? processedMessages = null) : base(
                instanceId: instanceId, 
                triggerMessageId: triggerMessageId,
                correlationId: correlationId,
                descriptor: descriptor,
                processedMessages: processedMessages)
        {
            this.State = state;
        }

        public TS State { get; }    
    }
}