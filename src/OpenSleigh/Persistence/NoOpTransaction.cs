using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence;

[ExcludeFromCodeCoverage]
public class NoOpTransaction : ITransaction
{
    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    private static readonly Lazy<NoOpTransaction> _instance = new(() => new NoOpTransaction());
    public static NoOpTransaction Instance => _instance.Value;
}