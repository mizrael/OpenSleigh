using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using OpenSleigh.Transport.AzureServiceBus;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.E2E
{
    public class CosmosSqlEventBroadcastingScenario : EventBroadcastingScenario, IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public CosmosSqlEventBroadcastingScenario(DbFixture fixture)
        {
            _fixture = fixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var sqlCfg = new CosmosSqlConfiguration(_fixture.ConnectionString, _fixture.DbName);

            cfg.UseAzureServiceBusTransport(_fixture.AzureServiceBusConfiguration)
                .UseCosmosSqlPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();
    }
}