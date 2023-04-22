using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;

namespace OpenSleigh.E2ETests.InMemory
{
    public class InMemoryMultipleSagasSameMessagesScenario : MultipleSagasSameMessagesScenario
    {
        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            cfg.UseInMemoryPersistence()
                .UseInMemoryTransport();
        }
    }
}