using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;

namespace OpenSleigh.E2ETests.InMemory
{
    public class InMemorySimpleSagaScenario : SimpleSagaScenario
    {
        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            cfg.UseInMemoryPersistence()
                .UseInMemoryTransport();
        }
    }
}