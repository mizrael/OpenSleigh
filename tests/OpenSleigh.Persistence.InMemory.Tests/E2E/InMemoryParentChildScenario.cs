using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;

namespace OpenSleigh.Persistence.InMemory.Tests.E2E
{
    public class InMemoryParentChildScenario : ParentChildScenario
    {
        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg) => 
            cfg.UseInMemoryTransport()
                .UseInMemoryPersistence();

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) => cfg.UseInMemoryTransport();
    }

}
