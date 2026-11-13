using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.InMemory;

[SingleHost]
public class InMemorySimpleSagaScenario : SimpleSagaScenario
{
    public InMemorySimpleSagaScenario(ITestOutputHelper console) : base(console) { }

    protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
    {
        cfg.UseInMemoryPersistence()
            .UseInMemoryTransport();
    }
}
