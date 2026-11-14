namespace OpenSleigh.InMemory;

/// <summary>
/// an instance built from a stored saga. It remembers the version of the saga it was built from,
/// so that the repository can tell whether the saga changed before the instance locks it.
/// </summary>
internal interface IFoundSagaInstance
{
    object FoundVersion { get; }
}

internal sealed record FoundSagaInstance : SagaInstance, IFoundSagaInstance
{
    public FoundSagaInstance(
        string instanceId,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor,
        IEnumerable<ProcessedMessage> processedMessages,
        object foundVersion)
        : base(instanceId, triggerMessageId, correlationId, descriptor, processedMessages)
    {
        FoundVersion = foundVersion;
    }

    public object FoundVersion { get; }
}

internal sealed record FoundSagaInstance<TS> : SagaInstance<TS>, IFoundSagaInstance
{
    public FoundSagaInstance(
        string instanceId,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor,
        TS state,
        IEnumerable<ProcessedMessage> processedMessages,
        object foundVersion)
        : base(instanceId, triggerMessageId, correlationId, descriptor, state, processedMessages)
    {
        FoundVersion = foundVersion;
    }

    public object FoundVersion { get; }
}

internal interface IFoundSagaInstanceFactory
{
    ISagaInstance Create(
        object state,
        string instanceId,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor,
        IEnumerable<ProcessedMessage> processedMessages,
        object foundVersion);
}

internal sealed class FoundSagaInstanceFactory<TS> : IFoundSagaInstanceFactory
{
    public ISagaInstance Create(
        object state,
        string instanceId,
        string triggerMessageId,
        string correlationId,
        SagaDescriptor descriptor,
        IEnumerable<ProcessedMessage> processedMessages,
        object foundVersion)
        => new FoundSagaInstance<TS>(instanceId, triggerMessageId, correlationId, descriptor, (TS)state, processedMessages, foundVersion);
}
