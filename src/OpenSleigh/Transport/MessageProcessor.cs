using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport;

internal class MessageProcessor : IMessageProcessor
{
    private static readonly ConcurrentDictionary<Type, IMessageDispatcher> _dispatchers = new();

    private readonly ISagaDescriptorsResolver _sagaDescriptorsResolver;
    private readonly ISagaRunner _sagaRunner;

    public MessageProcessor(
        ISagaRunner sagaRunner,
        ISagaDescriptorsResolver sagaDescriptorsResolver,
        ISerializer serializer)
    {
        _sagaRunner = sagaRunner ?? throw new ArgumentNullException(nameof(sagaRunner));
        _sagaDescriptorsResolver = sagaDescriptorsResolver ?? throw new ArgumentNullException(nameof(sagaDescriptorsResolver));
    }

    public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outboxMessage);

        var dispatcher = _dispatchers.GetOrAdd(outboxMessage.MessageType, static t =>
            (IMessageDispatcher)Activator.CreateInstance(
                typeof(MessageDispatcher<>).MakeGenericType(t))!);

        var descriptors = _sagaDescriptorsResolver.Resolve(outboxMessage.Message);
        await dispatcher.DispatchAsync(outboxMessage, _sagaRunner, descriptors, cancellationToken)
                        .ConfigureAwait(false);
    }
}
