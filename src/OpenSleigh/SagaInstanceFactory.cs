using OpenSleigh.Transport;

namespace OpenSleigh;

public class SagaInstanceFactory : ISagaInstanceFactory
{
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

        return Create((dynamic)instance, messageContext, descriptor);
    }

    private static ISagaInstance Create<TS, TM>(TS state, IMessageContext<TM> messageContext, SagaDescriptor descriptor)
        where TM : IMessage
        => new SagaInstance<TS>(
#if NET9_0_OR_GREATER
            instanceId: Guid.CreateVersion7().ToString(),
#else
            instanceId: Guid.NewGuid().ToString(),
#endif
            triggerMessageId: messageContext.MessageId,
            correlationId: messageContext.CorrelationId,
            descriptor,
            state);
}