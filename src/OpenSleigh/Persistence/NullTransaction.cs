using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence;

[ExcludeFromCodeCoverage]
public class NullTransaction : ITransaction
{
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    => ValueTask.CompletedTask;

    private readonly static Lazy<NullTransaction> _instance = new();
    public static ITransaction Instance => _instance.Value;
}