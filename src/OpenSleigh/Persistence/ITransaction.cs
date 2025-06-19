namespace OpenSleigh.Persistence;

public interface ITransaction : IAsyncDisposable
{
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}
