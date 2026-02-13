namespace OpenSleigh.Queries;

public interface ISagaStateQuery
{
    ValueTask<SagaInstanceInfo?> GetByInstanceIdAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    ValueTask<SagaInstanceInfo?> GetByCorrelationIdAsync(
        string correlationId,
        string sagaType,
        CancellationToken cancellationToken = default);

    ValueTask<PagedResult<SagaInstanceInfo>> GetAllAsync(
        SagaQueryFilter? filter = null,
        CancellationToken cancellationToken = default);
}
