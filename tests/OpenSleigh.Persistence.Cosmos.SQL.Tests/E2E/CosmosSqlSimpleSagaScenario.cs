using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.E2E
{
    public class CosmosSqlSimpleSagaScenario : SimpleSagaScenario, IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public CosmosSqlSimpleSagaScenario(DbFixture fixture)
        {
            _fixture = fixture;
        }
    
        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var sqlCfg = new CosmosSqlConfiguration(_fixture.ConnectionString, _fixture.DbName);

            cfg.UseInMemoryTransport()
                .UseCosmosSqlPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) => 
            cfg.UseInMemoryTransport();
    }
}
