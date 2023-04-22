using OpenSleigh.Transport;

namespace OpenSleigh
{
    internal class NoOpSagaExecutionContext : ISagaExecutionContext
    {
        private NoOpSagaExecutionContext() { }

        public string TriggerMessageId { get; private set; }

        public string CorrelationId { get; private set; }

        public string InstanceId { get; private set; }

        public SagaDescriptor Descriptor { get; private set; }

        public IReadOnlyCollection<ProcessedMessage> ProcessedMessages => 
            (IReadOnlyCollection<ProcessedMessage>)Enumerable.Empty<ProcessedMessage>();

        public bool IsCompleted => true;

        public string LockId => string.Empty;

        public static ISagaExecutionContext Create<TM>(IMessageContext<TM> messageContext, SagaDescriptor descriptor) where TM : IMessage
        => new NoOpSagaExecutionContext()
        {
            Descriptor = descriptor,
            TriggerMessageId = messageContext.Id,
            CorrelationId = messageContext.CorrelationId,
            InstanceId = Guid.NewGuid().ToString()            
        };

        public bool CanProcess<TM>(IMessageContext<TM> messageContext) where TM : IMessage
            => false;

        public ValueTask LockAsync(ISagaStateRepository sagaStateRepository, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public void MarkAsCompleted()
        {            
        }

        public ValueTask ProcessAsync<TM>(
            IMessageHandlerManager messageHandlerManager, 
            IMessageContext<TM> messageContext,
            ISagaExecutionService sagaExecutionService, 
            CancellationToken cancellationToken) where TM : IMessage
            => ValueTask.CompletedTask;

        public void SetAsProcessed<TM>(IMessageContext<TM> messageContext) where TM : IMessage
        {            
        }
    }
}