using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.InMemory;

public class InMemoryIdempotentMessageScenario : IdempotentMessageScenario
{
    public InMemoryIdempotentMessageScenario(ITestOutputHelper console) : base(console, 1)
    {
    }

    protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
    {
        cfg.UseInMemoryPersistence()
            .UseInMemoryTransport();
    }
}