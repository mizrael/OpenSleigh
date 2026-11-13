using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.InMemory.Messaging;

namespace OpenSleigh.E2ETests.Harness.Transports;

/// <summary>
/// nothing to create or remove: the channel and the dead letters live in the services of their host, and only there.
/// </summary>
internal sealed class InMemoryTransport : ITransport
{
    private readonly ConcurrentBag<InMemoryDeadLetters> _deadLetters = [];

    public void Configure(IBusConfigurator bus, string service) => bus.UseInMemoryTransport();

    public Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
    {
        _deadLetters.Add(hostServices.GetRequiredService<InMemoryDeadLetters>());
        return Task.CompletedTask;
    }

    // what is published before the subscriber reads waits in the channel
    public Task WaitUntilSubscribedAsync(IReadOnlyCollection<IServiceProvider> runningHosts, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public bool SupportsDeadLetters => true;

    // every host of the run, stopped ones included: what one of them gave up on stays given up on
    public Task<IReadOnlyDictionary<Type, long>> ReadDeadLettersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<Type, long>>(_deadLetters
            .SelectMany(deadLetters => deadLetters.Messages)
            .GroupBy(deadLetter => deadLetter.Message.MessageType)
            .ToDictionary(messages => messages.Key, messages => messages.LongCount()));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
