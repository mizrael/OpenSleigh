namespace OpenSleigh
{
    public interface ISagaStateRepository
    {
        ValueTask<ISagaExecutionContext?> FindAsync(SagaDescriptor descriptor, string correlationId, CancellationToken cancellationToken = default);
        ValueTask<string> LockAsync(ISagaExecutionContext state, CancellationToken cancellationToken = default);
        ValueTask ReleaseAsync(ISagaExecutionContext state, CancellationToken cancellationToken = default);
    }
}