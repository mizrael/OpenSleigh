using System;
using OpenSleigh.Core;

namespace OpenSleigh.Transport.RabbitMQ
{
    //TODO: tests
    public class QueueReferenceFactory : IQueueReferenceFactory
    {
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
            return Create(messageType);
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