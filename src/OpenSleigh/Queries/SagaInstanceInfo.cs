namespace OpenSleigh.Queries;

public record SagaInstanceInfo
{
    public required string InstanceId { get; init; }
    public required string CorrelationId { get; init; }
    public required string TriggerMessageId { get; init; }
    public required string SagaType { get; init; }
    public string? SagaStateType { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsLocked { get; init; }
    public IReadOnlyList<ProcessedMessageInfo> ProcessedMessages { get; init; } = [];

    /// <summary>
    /// The saga's custom state data, or null for stateless sagas.
    /// For InMemory persistence this is the live state object.
    /// For SQL/Mongo persistence this is the deserialized state object.
    /// </summary>
    public object? StateData { get; init; }
}
