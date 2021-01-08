using System;
using System.Collections.Concurrent;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    internal class QueueReferenceFactory : IQueueReferenceFactory
    {
        private readonly ConcurrentDictionary<Type, QueueReferences> _queueReferencesMap = new();
        
        public QueueReferences Create<TM>() where TM : IMessage
        {
            var messageType = typeof(TM);
            return Create(messageType);
        }

        public QueueReferences Create(IMessage message)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            
            var messageType = message.GetType();

            var references = _queueReferencesMap.GetOrAdd(messageType, k => Create(messageType));

            return references;
        }
        
        private static QueueReferences Create(Type messageType)
        {
            var exchangeName = messageType.Name.ToLower();
            var queueName = exchangeName + ".workers";
            var dlExchangeName = exchangeName + ".dead";
            var dlQueueName = dlExchangeName + ".workers";
            return new QueueReferences(exchangeName, queueName, dlExchangeName, dlQueueName);
        }

    }
}