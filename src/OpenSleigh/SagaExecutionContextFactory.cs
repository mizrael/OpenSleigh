using OpenSleigh.Messaging;

namespace OpenSleigh
{
    public class SagaExecutionContextFactory : ISagaExecutionContextFactory
    {
        public ISagaExecutionContext CreateState<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext)
            where TM : IMessage
        {
            if (descriptor is null)
                throw new ArgumentNullException(nameof(descriptor));
            if (messageContext is null)
                throw new ArgumentNullException(nameof(messageContext));

            if(descriptor.SagaStateType is null)            
                return new SagaExecutionContext(
                    instanceId: Guid.NewGuid().ToString(), 
                    triggerMessageId: messageContext.Id, 
                    correlationId: messageContext.CorrelationId,
                    descriptor: descriptor);
                        
            var instance = Activator.CreateInstance(descriptor.SagaStateType);
            if (instance is null)
                throw new TypeLoadException($"unable to create instance of type '{descriptor.SagaStateType.FullName}'");

            return Create((dynamic)instance, messageContext, descriptor);
        }

        private static ISagaExecutionContext Create<TS, TM>(TS state, IMessageContext<TM> messageContext, SagaDescriptor descriptor)
            where TM : IMessage
            => new SagaExecutionContext<TS>(
                instanceId: Guid.NewGuid().ToString(),
                triggerMessageId: messageContext.Id,
                correlationId: messageContext.CorrelationId,
                descriptor, 
                state);
    }
}