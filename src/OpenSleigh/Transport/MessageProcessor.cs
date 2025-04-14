using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport;

internal class MessageProcessor : IMessageProcessor
{
    private readonly ISagaDescriptorsResolver _sagaDescriptorsResolver;
    private readonly ISagaRunner _sagaRunner;
    private readonly ISerializer _serializer;
    
    public MessageProcessor(
        ISagaRunner sagaRunner, 
        ISagaDescriptorsResolver sagaDescriptorsResolver,
        ISerializer serializer)
    {
        _sagaRunner = sagaRunner ?? throw new ArgumentNullException(nameof(sagaRunner));
        _sagaDescriptorsResolver = sagaDescriptorsResolver ?? throw new ArgumentNullException(nameof(sagaDescriptorsResolver));

        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outboxMessage);

        var messageContext = ToContext((dynamic)outboxMessage.Message, outboxMessage);

        var descriptors = _sagaDescriptorsResolver.Resolve(outboxMessage.Message);
        foreach(var descriptor in descriptors) {
            try
            {
                await _sagaRunner.ProcessAsync(messageContext, descriptor, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (SagaException)
            {
                // TODO: send outboxMessage + descriptor to deadletter   
            }            
        }
    }

    private static IMessageContext<TM> ToContext<TM>(TM message, MessageEnvelope outboxMessage)
        where TM : IMessage
    {
        return MessageContext<TM>.Create(message, outboxMessage);
    }
}