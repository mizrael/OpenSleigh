using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.E2E
{
    public class SqlParentChildScenario : ParentChildScenario, IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public SqlParentChildScenario(DbFixture fixture)
        {
            _fixture = fixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var sqlCfg = new SqlConfiguration(_fixture.ConnectionString);

            cfg.UseInMemoryTransport()
                .UseSqlPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseInMemoryTransport();
    }

}
