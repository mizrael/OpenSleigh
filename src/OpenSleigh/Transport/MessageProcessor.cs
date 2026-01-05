using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport;

internal class MessageProcessor : IMessageProcessor
{
    private static readonly ConcurrentDictionary<Type, IProcessorWrapper> _handlers = new();

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

        var wrapper = _handlers.GetOrAdd(outboxMessage.MessageType, CreateProcessorWrapper);

        var descriptors = _sagaDescriptorsResolver.Resolve(outboxMessage.Message);
        foreach(var descriptor in descriptors) {
            try
            {
                await wrapper.ProcessAsync(_sagaRunner, outboxMessage, descriptor, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (SagaException)
            {
                // TODO: send outboxMessage + descriptor to deadletter   
            }            
        }
    }

    private static IProcessorWrapper CreateProcessorWrapper(Type messageType)
    {
        var handlerType = typeof(ProcessorWrapper<>).MakeGenericType(messageType);
        return (IProcessorWrapper)Activator.CreateInstance(handlerType)!;
    }

    private interface IProcessorWrapper
    {
        ValueTask ProcessAsync(ISagaRunner sagaRunner, MessageEnvelope outboxMessage, SagaDescriptor descriptor, CancellationToken cancellationToken);
    }

    private sealed class ProcessorWrapper<TM> : IProcessorWrapper where TM : IMessage
    {
        public ValueTask ProcessAsync(ISagaRunner sagaRunner, MessageEnvelope outboxMessage, SagaDescriptor descriptor, CancellationToken cancellationToken)
        {
            var messageContext = DefaultMessageContext<TM>.Create(outboxMessage);
            return sagaRunner.ProcessAsync(messageContext, descriptor, cancellationToken);
        }
    }
}