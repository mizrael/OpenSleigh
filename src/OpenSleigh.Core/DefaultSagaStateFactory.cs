using System;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    internal class DefaultSagaStateFactory<TD> : ISagaStateFactory<TD> 
        where TD : SagaState
    {
        private readonly IServiceProvider _serviceProvider;

        public DefaultSagaStateFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public TD Create(IMessage message)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            
            var messageType = message.GetType();
            var stateType = typeof(TD);

            var factoryInterfaceType = typeof(ISagaStateFactory<,>).MakeGenericType(messageType, stateType);
            var factory = _serviceProvider.GetService(factoryInterfaceType) as ISagaStateFactory<TD>;
            if (null == factory)
                throw new StateCreationException(
                    $"no state factory registered for message type '{message.GetType().FullName}'");
            
            var state = factory.Create((dynamic) message);
            return state;
        }
    }
}