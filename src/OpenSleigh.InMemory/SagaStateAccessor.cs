namespace OpenSleigh.InMemory;

/// <summary>
/// reads the state of a saga instance, and builds an instance around a state, without knowing the type of the state.
/// </summary>
internal interface ISagaStateAccessor
{
    object? GetState(ISagaInstance instance);

    /// <summary>an instance of the same saga as <paramref name="source"/>, with another state and other processed messages.</summary>
    ISagaInstance CreateInstance(ISagaInstance source, object? state, IEnumerable<ProcessedMessage> processedMessages);
}

internal sealed class SagaStateAccessor<TS> : ISagaStateAccessor
{
    public object? GetState(ISagaInstance instance)
        => instance is ISagaInstance<TS> withState ? withState.State : null;

    public ISagaInstance CreateInstance(ISagaInstance source, object? state, IEnumerable<ProcessedMessage> processedMessages)
        => new SagaInstance<TS>(
            source.InstanceId,
            source.TriggerMessageId,
            source.CorrelationId,
            source.Descriptor,
            (TS)state!,
            processedMessages);
}
