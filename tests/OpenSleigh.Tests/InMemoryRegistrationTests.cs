using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

/// <summary>
/// what UseInMemoryPersistence and UseInMemoryTransport register for the outbox relay.
/// </summary>
public class InMemoryRegistrationTests
{
    [Fact]
    public void UseInMemoryPersistence_should_register_what_the_outbox_relay_needs_with_another_transport()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Substitute.For<IPublisher>())
            .AddOpenSleigh(cfg => cfg.UseInMemoryPersistence())
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<NoOpTransactionManager>(scope.ServiceProvider.GetService<ITransactionManager>());
        Assert.NotNull(scope.ServiceProvider.GetService<IOutboxProcessor>());
    }

    [Fact]
    public void UseInMemoryTransport_should_keep_the_transaction_manager_of_a_persistence_configured_before_it()
    {
        var registeredByThePersistence = Substitute.For<ITransactionManager>();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddOpenSleigh(cfg =>
            {
                cfg.Services.AddSingleton(registeredByThePersistence);
                cfg.UseInMemoryTransport();
            })
            .BuildServiceProvider();

        Assert.Same(registeredByThePersistence, provider.GetRequiredService<ITransactionManager>());
    }

    [Fact]
    public void UseInMemoryTransport_should_register_a_transaction_manager_when_the_persistence_has_none()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddOpenSleigh(cfg => cfg.UseInMemoryTransport())
            .BuildServiceProvider();

        Assert.IsType<NoOpTransactionManager>(provider.GetService<ITransactionManager>());
    }
}
