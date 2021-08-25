using OpenSleigh.Core.Messaging;
using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Linq;

namespace OpenSleigh.Transport.Kafka
{
    public class QueueReferenceFactory : IQueueReferenceFactory
    {
        private readonly ConcurrentDictionary<Type, QueueReferences> _queueReferencesCache = new();
        private readonly Func<Type, QueueReferences> _defaultCreator;
        private readonly IServiceProvider _sp;

        public QueueReferenceFactory(IServiceProvider sp, Func<Type, QueueReferences> defaultCreator = null)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));

            _defaultCreator = defaultCreator ?? (messageType =>
            {
                var topicName = messageType.Name.ToLower();
                return new QueueReferences(topicName, $"{topicName}.dead");
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

        public Type GetQueueType(string topic) =>
            _queueReferencesCache.SingleOrDefault(pair => pair.Value.TopicName.Equals(topic)).Key;

    }
}