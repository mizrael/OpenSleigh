using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.Transport
{
    internal class DefaultMessageHandlerFactory : IMessageHandlerFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public DefaultMessageHandlerFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IHandleMessage<TM> Create<TM>(ISagaExecutionContext context) 
            where TM : IMessage
        {
            var instance = ActivatorUtilities.CreateInstance(_serviceProvider, context.Descriptor.SagaType, context);
            if (instance is null)
                throw new TypeLoadException($"unable to create Saga instance with type '{context.Descriptor.SagaType}'");

            var handler = instance as IHandleMessage<TM>;
            if (handler is null)
                throw new InvalidCastException($"type '{context.Descriptor.SagaType}' does not implement '{nameof(ISaga)}'");

            return handler;
        }
    }
}