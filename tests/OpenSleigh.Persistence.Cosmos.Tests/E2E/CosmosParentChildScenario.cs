using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Persistence.Cosmos.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.Tests.E2E
{
    public class CosmosParentChildScenario : ParentChildScenario, IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public CosmosParentChildScenario(DbFixture fixture)
        {
            _fixture = fixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var cosmosCfg = new CosmosConfiguration(_fixture.ConnectionString,
                _fixture.DbName,
                CosmosSagaStateRepositoryOptions.Default,
                CosmosOutboxRepositoryOptions.Default);

            cfg.UseInMemoryTransport()
                .UseCosmosPersistence(cosmosCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseInMemoryTransport();
    }

}
