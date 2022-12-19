using OpenSleigh.Outbox;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class QueueReferenceFactory : IQueueReferenceFactory
    {
        private readonly ConcurrentDictionary<Type, QueueReferences> _queueReferencesCache = new();
        private readonly Func<Type, QueueReferences> _factory;
        private readonly ISystemInfo _systemInfo;

        public QueueReferenceFactory(ISystemInfo systemInfo,
                                    Func<Type, QueueReferences>? factory = null)
        {
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));

            _factory = factory ?? (messageType =>
                {
                    var exchangeName = messageType.Name.ToLower();

                    var queueName = $"{exchangeName}.{_systemInfo.ClientGroup}.workers";

                    var dlExchangeName = exchangeName + ".dead";

                    var dlQueueName = $"{dlExchangeName}.{_systemInfo.ClientGroup}.workers";

                    return new QueueReferences(exchangeName, queueName, exchangeName, dlExchangeName, dlQueueName);
                });
        }

        public QueueReferences Create(OutboxMessage message)
            => _queueReferencesCache.GetOrAdd(message.MessageType, k => _factory(message.MessageType));

        public QueueReferences Create<TM>() where TM : IMessage
            => _queueReferencesCache.GetOrAdd(typeof(TM), k => _factory(typeof(TM)));
    }
}