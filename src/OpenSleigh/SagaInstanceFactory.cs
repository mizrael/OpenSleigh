using OpenSleigh.Transport;
using System.Reflection;

namespace OpenSleigh;

public class SagaInstanceFactory : ISagaInstanceFactory
{
    private static readonly MethodInfo _createMethod = typeof(SagaInstanceFactory)
        .GetMethod(nameof(CreateSagaInstance), BindingFlags.NonPublic | BindingFlags.Static)!;

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

        var genericMethod = _createMethod.MakeGenericMethod(descriptor.SagaStateType);
        return (ISagaInstance)genericMethod.Invoke(null, new object[] { instance, messageContext.MessageId, messageContext.CorrelationId, descriptor })!;
    }

    private static ISagaInstance CreateSagaInstance<TS>(TS state, string messageId, string correlationId, SagaDescriptor descriptor)
        => new SagaInstance<TS>(
#if NET9_0_OR_GREATER
            instanceId: Guid.CreateVersion7().ToString(),
#else
            instanceId: Guid.NewGuid().ToString(),
#endif
            triggerMessageId: messageId,
            correlationId: correlationId,
            descriptor,
            state);
}