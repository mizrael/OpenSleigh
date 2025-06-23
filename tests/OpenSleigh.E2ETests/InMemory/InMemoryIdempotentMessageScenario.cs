using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;

namespace OpenSleigh.E2ETests.InMemory;

public class InMemoryIdempotentMessageScenario : IdempotentMessageScenario
{
    public InMemoryIdempotentMessageScenario() : base(1)
    {
    }

    protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
    {
        cfg.UseInMemoryPersistence()
            .UseInMemoryTransport();
    }
}