using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.Outbox;

namespace OpenSleigh.E2ETests.Harness.Stores;

/// <summary>
/// nothing to create or remove: the store lives in the services of its host, and only there.
/// </summary>
internal sealed class InMemoryStore : IStore
{
    public void Configure(IBusConfigurator bus) => bus.UseInMemoryPersistence();

    public Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken) => Task.CompletedTask;

    // reading the pending messages of the in-memory outbox claims nothing: it can be used to count them
    public async Task<long> CountOutboxMessagesAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
    {
        var outbox = hostServices.GetRequiredService<IOutboxRepository>();
        var pending = await outbox.ReadPendingAsync(cancellationToken);
        return pending.LongCount();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
