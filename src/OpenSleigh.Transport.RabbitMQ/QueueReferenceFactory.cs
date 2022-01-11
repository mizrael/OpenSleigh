using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class QueueReferenceFactory : IQueueReferenceFactory
    {
        private readonly ConcurrentDictionary<Type, QueueReferences> _queueReferencesCache = new();
        private readonly Func<Type, QueueReferences> _defaultCreator;
        private readonly IServiceProvider _sp;
        private readonly ISystemInfo _systemInfo;
        
        public QueueReferenceFactory(IServiceProvider sp, 
                                    ISystemInfo systemInfo, 
                                    Func<Type, QueueReferences> defaultCreator = null)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));

            _defaultCreator = defaultCreator ?? (messageType =>
            {
                var exchangeName = messageType.Name.ToLower();
                
                var isEvent = messageType.IsEvent();
                
                var queueName = isEvent ? 
                    $"{exchangeName}.{_systemInfo.ClientGroup}.workers" : 
                    $"{exchangeName}.workers";
                
                var dlExchangeName = exchangeName + ".dead";
                
                var dlQueueName = isEvent ? 
                    $"{dlExchangeName}.{_systemInfo.ClientGroup}.workers" : 
                    $"{dlExchangeName}.workers";

                // if it's an Event, we use the exchange name as routing key,
                // this way all the bond queues will receive it.
                // otherwise we are expecting a single queue to be connected
                // to the exchange, so we use the queue name to prevent duplicate handling
                var routingKey = isEvent ? exchangeName : queueName;
                
                return new QueueReferences(exchangeName, queueName, routingKey, dlExchangeName, dlQueueName);
            });
        }

        public QueueReferences Create<TM>(TM message = default) where TM : IMessage
            => _queueReferencesCache.GetOrAdd(typeof(TM), k => CreateCore<TM>());
        
        private QueueReferences CreateCore<TM>()
            where TM : IMessage
        {
            var creator = _sp.GetService<QueueReferencesPolicy<TM>>();
            return (creator is null) ? _defaultCreator(typeof(TM)) : creator();
        }
    }

    public delegate QueueReferences QueueReferencesPolicy<TM>() where TM : IMessage;
}