using OpenSleigh.Outbox;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.Kafka;

public delegate QueueReferences QueueReferencesCreator(Type messageType);

public class QueueReferenceFactory : IQueueReferenceFactory
{
    private readonly ConcurrentDictionary<Type, QueueReferences> _queueReferencesCache = new();
    private readonly QueueReferencesCreator _creator;

    public QueueReferenceFactory(QueueReferencesCreator? creator = null)
    {
        _creator = creator ?? (messageType =>
        {
            var topicName = messageType.Name.ToLower();
            return new QueueReferences(topicName, $"{topicName}.dead");
        });
    }

    public QueueReferences Create(MessageEnvelope message)
        => _queueReferencesCache.GetOrAdd(message.MessageType, k => _creator(message.MessageType));

    public QueueReferences Create<TM>() where TM : IMessage
        => _queueReferencesCache.GetOrAdd(typeof(TM), k => _creator(typeof(TM)));

    public QueueReferences Create(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        if (messageType.IsAssignableTo(typeof(IMessage)) == false)
            throw new ArgumentException($"type '{messageType.FullName}' does not implement IMessage interface", nameof(messageType));

        return _queueReferencesCache.GetOrAdd(messageType, k => _creator(messageType));
    }
}