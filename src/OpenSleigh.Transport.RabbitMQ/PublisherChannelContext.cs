using System;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    public sealed record PublisherChannelContext(IModel Channel, QueueReferences QueueReferences) : IDisposable
    {
        public void Dispose()
        {
            if (Channel is null)
                return;
            if(Channel.IsOpen)
                Channel.Close();
            Channel.Dispose();
        }
    }
}