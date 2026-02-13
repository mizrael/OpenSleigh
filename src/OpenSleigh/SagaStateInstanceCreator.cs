namespace OpenSleigh;

internal interface ISagaStateInstanceCreator
{
    ISagaInstance Create(
        object state,
        string instanceId,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor);
}

internal sealed class SagaStateInstanceCreator<TS> : ISagaStateInstanceCreator
{
    public ISagaInstance Create(
        object state,
        string instanceId,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor)
        => new SagaInstance<TS>(
            instanceId: instanceId,
            triggerMessageId: triggerMessageId,
            correlationId: correlationId,
            descriptor: descriptor,
            state: (TS)state);
}
