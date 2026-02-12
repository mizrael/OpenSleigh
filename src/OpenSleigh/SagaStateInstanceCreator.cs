namespace OpenSleigh;

internal interface ISagaStateInstanceCreator
{
    ISagaInstance Create(
        object state,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor);
}

internal sealed class SagaStateInstanceCreator<TS> : ISagaStateInstanceCreator
{
    public ISagaInstance Create(
        object state,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor)
        => new SagaInstance<TS>(
#if NET9_0_OR_GREATER
            instanceId: Guid.CreateVersion7().ToString(),
#else
            instanceId: Guid.NewGuid().ToString(),
#endif
            triggerMessageId: triggerMessageId,
            correlationId: correlationId,
            descriptor: descriptor,
            state: (TS)state);
}
