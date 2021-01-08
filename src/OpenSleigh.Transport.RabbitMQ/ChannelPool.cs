using System;
using System.Collections.Concurrent;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    internal class ChannelPool : IChannelPool, IDisposable
    {
        private readonly IBusConnection _connection;
        private readonly ConcurrentDictionary<string, ConcurrentBag<IModel>> _pools = new ();
        
        public ChannelPool(IBusConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public IModel Get(QueueReferences references)
        {
            if (references == null) 
                throw new ArgumentNullException(nameof(references));

            var pool = _pools.GetOrAdd(references.ExchangeName, _ => new());
            
            if (!pool.TryTake(out var channel))
            {
                channel = _connection.CreateChannel();
                channel.ExchangeDeclare(exchange: references.ExchangeName, type: ExchangeType.Fanout);
            }

            return channel;
        }

        public void Return(IModel channel, QueueReferences references)
        {
            if (channel == null) 
                throw new ArgumentNullException(nameof(channel));
            if (references == null) 
                throw new ArgumentNullException(nameof(references));
            
            var pool = _pools.GetOrAdd(references.ExchangeName, _ => new());
            pool.Add(channel);
        }

        public void Dispose()
        {
            foreach (var pool in _pools.Values)
            {
                foreach (var channel in pool)
                {
                    if(channel.IsOpen)
                        channel.Close();
                    channel.Dispose();
                }
                pool.Clear();
            }
            _pools.Clear();
        }
    }
}