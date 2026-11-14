namespace OpenSleigh.Queries;

/// <summary>
/// reads the records of the messages whose handler failed.
/// </summary>
public interface ISagaFailureQuery
{
    /// <returns>a page of the failures that match the filter, most recent first.</returns>
    ValueTask<PagedResult<SagaMessageFailure>> GetAllAsync(
        SagaFailureQueryFilter? filter = null,
        CancellationToken cancellationToken = default);
}

public record SagaFailureQueryFilter
{
    public string? InstanceId { get; init; }
    public string? CorrelationId { get; init; }
    public string? SagaType { get; init; }
    public string? MessageType { get; init; }
    public FailureDecision? Decision { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// whether a failure is one the filter asks for.
    /// </summary>
    public bool Matches(SagaMessageFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return (InstanceId is null || failure.InstanceId == InstanceId)
            && (CorrelationId is null || failure.CorrelationId == CorrelationId)
            && (SagaType is null || failure.SagaType == SagaType)
            && (MessageType is null || failure.MessageType == MessageType)
            && (Decision is null || failure.Decision == Decision);
    }
}

/// <summary>
/// removes old failure records. Nothing removes them otherwise.
/// </summary>
public interface ISagaFailurePurger
{
    /// <summary>
    /// removes the records of the failures that occurred before <paramref name="olderThan"/>.
    /// </summary>
    /// <returns>how many records were removed.</returns>
    ValueTask<long> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
