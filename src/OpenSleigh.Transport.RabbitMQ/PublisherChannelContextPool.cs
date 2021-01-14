using System;
using System.Collections.Concurrent;
using System.Linq;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    internal sealed class PublisherChannelContextPool : IPublisherChannelContextPool, IDisposable
    {
        private readonly IBusConnection _connection;
        private readonly ConcurrentDictionary<string, ConcurrentBag<PublisherChannelContext>> _pools = new ();
        
        public PublisherChannelContextPool(IBusConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public PublisherChannelContext Get(QueueReferences references)
        {
            if (references == null) 
                throw new ArgumentNullException(nameof(references));

            var pool = _pools.GetOrAdd(references.ExchangeName, _ => new());
            
            if (!pool.TryTake(out var ctx))
            {
                var channel = _connection.CreateChannel();
                channel.ExchangeDeclare(exchange: references.ExchangeName, type: ExchangeType.Topic);
                ctx = new PublisherChannelContext(channel, references, this);
            }

            return ctx;
        }

        public void Return(PublisherChannelContext ctx)
        {
            if (ctx == null) 
                throw new ArgumentNullException(nameof(ctx));

            if (ctx.Channel.IsClosed)
                return;
            
            var pool = _pools.GetOrAdd(ctx.QueueReferences.ExchangeName, _ => new());
            pool.Add(ctx);
        }

        public int GetAvailableCount() => _pools.Sum(p => p.Value.Count);
        
        public void Dispose()
        {
            foreach (var pool in _pools.Values)
            {
                foreach (var ctx in pool)
                {
                    if(ctx.Channel.IsOpen)
                        ctx.Channel.Close();
                    ctx.Channel.Dispose();
                }
                pool.Clear();
            }
            _pools.Clear();
        }
    }
}