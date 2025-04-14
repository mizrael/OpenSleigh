using OpenSleigh.Outbox;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ;

public delegate QueueReferences QueueReferencesCreator(Type messageType);

public class QueueReferenceFactory : IQueueReferenceFactory
{
    private readonly ConcurrentDictionary<Type, QueueReferences> _queueReferencesCache = new();
    private readonly QueueReferencesCreator _factory;

    public QueueReferenceFactory(QueueReferencesCreator creator)
    {
        _factory = creator ?? throw new ArgumentNullException(nameof(creator));
    }

    public QueueReferences Create(MessageEnvelope message)
        => _queueReferencesCache.GetOrAdd(message.MessageType, k => _factory(message.MessageType));

    public QueueReferences Create<TM>() where TM : IMessage
        => _queueReferencesCache.GetOrAdd(typeof(TM), k => _factory(typeof(TM)));

    public IEnumerable<QueueReferences> RegisteredQueueReferences => _queueReferencesCache.Values;


    public static QueueReferencesCreator BuildDefaultCreator(ISystemInfo sysInfo)
    => messageType =>
    {
        var exchangeName = messageType.Name.ToLower();
        var queueName = $"{exchangeName}.{sysInfo.ClientGroup}.workers";

        var dlExchangeName = exchangeName + ".dead";

        var dlQueueName = $"{dlExchangeName}.{sysInfo.ClientGroup}.workers";
        return new QueueReferences(exchangeName, queueName, exchangeName, dlExchangeName, dlQueueName);
    };
}    