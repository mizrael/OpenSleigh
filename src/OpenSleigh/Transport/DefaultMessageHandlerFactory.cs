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

        public IHandleMessage<TM> Create<TM>(Type sagaType, object state) 
            where TM : IMessage
        {
            var instance = ActivatorUtilities.CreateInstance(_serviceProvider, sagaType, state);
            if (instance is null)
                throw new TypeLoadException($"unable to create Saga instance with type '{sagaType.FullName}'");

            var handler = instance as IHandleMessage<TM>;
            if (handler is null)
                throw new InvalidCastException($"type '{sagaType.FullName}' does not implement '{nameof(ISaga)}'");

            return handler;
        }
    }
}