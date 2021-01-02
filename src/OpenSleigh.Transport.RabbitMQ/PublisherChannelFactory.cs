using System;
using System.Collections.Concurrent;
using OpenSleigh.Core;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    //TODO: tests
    public class PublisherChannelFactory : IPublisherChannelFactory
    {
        private readonly IBusConnection _connection;
        private readonly IQueueReferenceFactory _queueReferenceFactory;

        private readonly ConcurrentDictionary<Type, QueueReferences> _queueReferencesMap = new();

        public PublisherChannelFactory(IBusConnection connection, IQueueReferenceFactory queueReferenceFactory)
        {
            _connection = connection;
            _queueReferenceFactory = queueReferenceFactory;
        }

        public PublisherChannelContext Create(IMessage message)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));

            var messageType = message.GetType();
            var references = _queueReferencesMap.GetOrAdd(messageType, k => _queueReferenceFactory.Create(message));
            var channel = _connection.CreateChannel();
            channel.ExchangeDeclare(exchange: references.ExchangeName, type: ExchangeType.Fanout);
            return new PublisherChannelContext(channel, references);
        }
    }
}