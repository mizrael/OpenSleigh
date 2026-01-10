using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.InMemory;

public class InMemorySequentialMultiStartSagaScenario : SequentialMultiStartSagaScenario
{
    public InMemorySequentialMultiStartSagaScenario(ITestOutputHelper console) : base(console) { }

    protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
    {
        cfg.UseInMemoryPersistence()
            .UseInMemoryTransport();
    }
}
