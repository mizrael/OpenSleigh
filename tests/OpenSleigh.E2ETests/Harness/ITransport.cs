using OpenSleigh.DependencyInjection;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// the transport of one scenario run: its queues or topics are named after the run.
/// Disposing it removes what it created.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <param name="service">the service the host belongs to: hosts of one service compete for its messages,
    /// each service gets its own copy of a message.</param>
    void Configure(IBusConfigurator bus, string service);

    /// <summary>
    /// called for every host, once it is built and before it starts.
    /// </summary>
    Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken);

    /// <summary>false when the transport has nowhere to put a message it gives up on.</summary>
    bool SupportsDeadLetters { get; }

    /// <summary>
    /// how many messages were dead-lettered, over the message types the hosts of this run handle.
    /// </summary>
    /// <exception cref="NotSupportedException">the transport has no dead letters.</exception>
    Task<long> CountDeadLettersAsync(CancellationToken cancellationToken);
}
