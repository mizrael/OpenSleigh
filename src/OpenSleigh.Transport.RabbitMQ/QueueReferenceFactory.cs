using OpenSleigh.Core;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class QueueReferenceFactory : IQueueReferenceFactory
    {
        public QueueReferences Create<TM>() where TM : IMessage
        {
            var messageType = typeof(TM);
            var exchangeName = messageType.Name.ToLower();
            var queueName = exchangeName + ".workers";
            var dlExchangeName = exchangeName + ".dead";
            var dlQueueName = dlExchangeName + ".workers";
            return new QueueReferences(exchangeName, queueName, dlExchangeName, dlQueueName);
        }
    }
}