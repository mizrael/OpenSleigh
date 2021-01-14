using System;
using System.Runtime.CompilerServices;
using OpenSleigh.Core.Messaging;

[assembly: InternalsVisibleTo("OpenSleigh.Transport.RabbitMQ.Tests")]
namespace OpenSleigh.Transport.RabbitMQ
{
    internal class PublisherChannelFactory : IPublisherChannelFactory
    {
        private readonly IPublisherChannelContextPool _publisherChannelContextPool;
        private readonly IQueueReferenceFactory _queueReferenceFactory;
        
        public PublisherChannelFactory(IPublisherChannelContextPool publisherChannelContextPool, IQueueReferenceFactory queueReferenceFactory)
        {
            _publisherChannelContextPool = publisherChannelContextPool ?? throw new ArgumentNullException(nameof(publisherChannelContextPool));
            _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        }

        public PublisherChannelContext Create(IMessage message)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            
            var references = _queueReferenceFactory.Create((dynamic)message);
            var ctx = _publisherChannelContextPool.Get(references);
            return ctx;
        }
    }
}