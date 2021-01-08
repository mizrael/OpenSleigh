using System;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    public sealed class PublisherChannelContext : IDisposable
    {
        private readonly IChannelPool _channelPool;

        public PublisherChannelContext(IModel channel, 
            QueueReferences queueReferences,
            IChannelPool channelPool)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            QueueReferences = queueReferences ?? throw new ArgumentNullException(nameof(queueReferences));
            _channelPool = channelPool ?? throw new ArgumentNullException(nameof(channelPool));
        }

        public IModel Channel { get; }
        public QueueReferences QueueReferences { get; }

        public void Dispose()
        {
            if (Channel is null)
                return;
            _channelPool.Return(this.Channel, this.QueueReferences);
        }
    }
}