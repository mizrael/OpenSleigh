using OpenSleigh.DependencyInjection;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// the persistence of one scenario run: it owns a database of its own, created for the run and removed with it.
/// Disposing it removes what it created.
/// </summary>
public interface IStore : IAsyncDisposable
{
    void Configure(IBusConfigurator bus);

    /// <summary>
    /// called for every host, once it is built and before it starts.
    /// </summary>
    Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken);

    /// <summary>
    /// how many messages are in the outbox, relayed or not, read through the services of a host.
    /// </summary>
    Task<long> CountOutboxMessagesAsync(IServiceProvider hostServices, CancellationToken cancellationToken);
}
