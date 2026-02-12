using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh;

public class SagaInstanceFactory : ISagaInstanceFactory
{
    private static readonly ConcurrentDictionary<Type, ISagaStateInstanceCreator> _creators = new();

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

        var instance = Activator.CreateInstance(descriptor.SagaStateType);
        if (instance is null)
            throw new TypeLoadException($"unable to create instance of type '{descriptor.SagaStateType.FullName}'");

        var creator = _creators.GetOrAdd(descriptor.SagaStateType, static t =>
            (ISagaStateInstanceCreator)Activator.CreateInstance(
                typeof(SagaStateInstanceCreator<>).MakeGenericType(t))!);

        return creator.Create(
            instance,
#if NET9_0_OR_GREATER
            Guid.CreateVersion7().ToString(),
#else
            Guid.NewGuid().ToString(),
#endif
            messageContext.MessageId,
            messageContext.CorrelationId,
            descriptor);
    }
}
