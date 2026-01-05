using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh;

public class SagaInstanceFactory : ISagaInstanceFactory
{
    private static readonly ConcurrentDictionary<Type, ISagaInstanceCreator> _creators = new();

    public ISagaInstance Create<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext)
        where TM : IMessage
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(messageContext);

        if (descriptor.SagaStateType is null)
            return new SagaInstance(
#if NET9_0_OR_GREATER
                instanceId: Guid.CreateVersion7().ToString(),
#else
                instanceId: Guid.NewGuid().ToString(),
#endif
                triggerMessageId: messageContext.MessageId,
                correlationId: messageContext.CorrelationId,
                descriptor: descriptor);

        var state = Activator.CreateInstance(descriptor.SagaStateType);
        if (state is null)
            throw new TypeLoadException($"unable to create instance of type '{descriptor.SagaStateType.FullName}'");

        var creator = _creators.GetOrAdd(descriptor.SagaStateType, CreateCreator);
        return creator.Create(state, messageContext.MessageId, messageContext.CorrelationId, descriptor);
    }

    private static ISagaInstanceCreator CreateCreator(Type stateType)
    {
        var creatorType = typeof(SagaInstanceCreator<>).MakeGenericType(stateType);
        var creator = Activator.CreateInstance(creatorType);
        if(creator is null)
            throw new InvalidOperationException($"Could not create saga instance for '{stateType.FullName}'.");
        return (ISagaInstanceCreator)creator;
    }

    private interface ISagaInstanceCreator
    {
        ISagaInstance Create(object state, string messageId, string correlationId, SagaDescriptor descriptor);
    }

    private sealed class SagaInstanceCreator<TS> : ISagaInstanceCreator
    {
        public ISagaInstance Create(object state, string messageId, string correlationId, SagaDescriptor descriptor)
            => new SagaInstance<TS>(
#if NET9_0_OR_GREATER
                instanceId: Guid.CreateVersion7().ToString(),
#else
                instanceId: Guid.NewGuid().ToString(),
#endif
                triggerMessageId: messageId,
                correlationId: correlationId,
                descriptor,
                (TS)state);
    }
}