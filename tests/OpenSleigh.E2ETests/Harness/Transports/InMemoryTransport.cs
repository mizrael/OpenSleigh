using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;

namespace OpenSleigh.E2ETests.Harness.Transports;

/// <summary>
/// nothing to create or remove: the channel lives in the services of its host, and only there.
/// </summary>
internal sealed class InMemoryTransport : ITransport
{
    public void Configure(IBusConfigurator bus, string service) => bus.UseInMemoryTransport();

    public Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken) => Task.CompletedTask;

    // what is published before the subscriber reads waits in the channel
    public Task WaitUntilSubscribedAsync(IServiceProvider hostServices, CancellationToken cancellationToken) => Task.CompletedTask;

    public bool SupportsDeadLetters => false;

    public Task<IReadOnlyDictionary<Type, long>> ReadDeadLettersAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException("the in-memory transport has no dead letters.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
