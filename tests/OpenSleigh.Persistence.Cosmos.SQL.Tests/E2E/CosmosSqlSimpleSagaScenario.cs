using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using Xunit;
using OpenSleigh.Transport.AzureServiceBus;
using OpenSleigh.Core.Tests.Sagas;
using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.E2E
{
    public class CosmosSqlSimpleSagaScenario : SimpleSagaScenario, IClassFixture<DbFixture>, IAsyncLifetime
    {
        private readonly DbFixture _fixture;

        private readonly string _topicName;
        private readonly string _subscriptionName;

        public CosmosSqlSimpleSagaScenario(DbFixture fixture)
        {
            _fixture = fixture;

            var messageName = nameof(StartSimpleSaga).ToLower();
            _topicName = $"{messageName}.tests.{Guid.NewGuid()}";
            _subscriptionName = $"{messageName}.workers";
        }
    
        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var sqlCfg = new CosmosSqlConfiguration(_fixture.ConnectionString, _fixture.DbName);

            cfg.UseAzureServiceBusTransport(_fixture.AzureServiceBusConfiguration, builder =>
            {
                builder.UseMessageNamingPolicy<StartSimpleSaga>(() =>
                    new QueueReferences(_topicName, _subscriptionName));
            })
                .UseCosmosSqlPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) => 
            cfg.UseAzureServiceBusTransport();

        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_fixture.AzureServiceBusConfiguration.ConnectionString);
            await adminClient.DeleteTopicAsync(_topicName);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
