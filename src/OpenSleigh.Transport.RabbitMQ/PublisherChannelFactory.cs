using System;
using System.Runtime.CompilerServices;
using OpenSleigh.Core.Messaging;

[assembly: InternalsVisibleTo("OpenSleigh.Transport.RabbitMQ.Tests")]
namespace OpenSleigh.Transport.RabbitMQ
{
    internal class PublisherChannelFactory : IPublisherChannelFactory
    {
        private readonly IChannelPool _channelPool;
        private readonly IQueueReferenceFactory _queueReferenceFactory;
        
        public PublisherChannelFactory(IChannelPool channelPool, IQueueReferenceFactory queueReferenceFactory)
        {
            _channelPool = channelPool ?? throw new ArgumentNullException(nameof(channelPool));
            _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        }

        public PublisherChannelContext Create(IMessage message)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            
            var references = _queueReferenceFactory.Create(message);
            var channel = _channelPool.Get(references);
            return new PublisherChannelContext(channel, references, _channelPool);
        }
    }
}