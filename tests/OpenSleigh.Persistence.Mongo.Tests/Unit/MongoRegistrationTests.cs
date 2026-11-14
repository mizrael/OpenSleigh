using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit;

/// <summary>
/// what UseMongoPersistence registers. Nothing here connects to a server.
/// </summary>
public class MongoRegistrationTests
{
    private static readonly MongoConfiguration Configuration = new("mongodb://127.0.0.1:27017", "registrationTests");

    [Fact]
    public void UseMongoPersistence_should_register_the_transaction_manager_the_outbox_relay_needs()
    {
        using var provider = new ServiceCollection()
            .AddOpenSleigh(cfg => cfg.UseMongoPersistence(Configuration))
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<ITransactionManager>());
    }

    [Fact]
    public void UseMongoPersistence_should_keep_a_transaction_manager_the_application_registered()
    {
        var registeredByTheApplication = Substitute.For<ITransactionManager>();

        using var provider = new ServiceCollection()
            .AddSingleton(registeredByTheApplication)
            .AddOpenSleigh(cfg => cfg.UseMongoPersistence(Configuration))
            .BuildServiceProvider();

        Assert.Same(registeredByTheApplication, provider.GetRequiredService<ITransactionManager>());
    }
}
