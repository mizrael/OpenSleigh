using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Reflection;

namespace OpenSleigh.Transport;

internal class MessageProcessor : IMessageProcessor
{
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

        var messageType = outboxMessage.MessageType;
        var messageContext = CreateMessageContext(messageType, outboxMessage);

        var descriptors = _sagaDescriptorsResolver.Resolve(outboxMessage.Message);
        foreach(var descriptor in descriptors) {
            try
            {
                await ProcessSagaAsync(messageContext, messageType, descriptor, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (SagaException)
            {
                // TODO: send outboxMessage + descriptor to deadletter   
            }            
        }
    }

    private static object CreateMessageContext(Type messageType, MessageEnvelope outboxMessage)
    {
        var contextType = typeof(DefaultMessageContext<>).MakeGenericType(messageType);
        var createMethod = contextType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        return createMethod.Invoke(null, new object[] { outboxMessage })!;
    }

    private ValueTask ProcessSagaAsync(object messageContext, Type messageType, SagaDescriptor descriptor, CancellationToken cancellationToken)
    {
        var processMethod = typeof(ISagaRunner).GetMethod(nameof(ISagaRunner.ProcessAsync))!
            .MakeGenericMethod(messageType);
        return (ValueTask)processMethod.Invoke(_sagaRunner, new object[] { messageContext, descriptor, cancellationToken })!;
    }
}