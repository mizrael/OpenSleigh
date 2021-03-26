using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using Xunit;
using OpenSleigh.Transport.AzureServiceBus;
using OpenSleigh.Core.Tests.Sagas;
using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures;
using OpenSleigh.Persistence.Cosmos.SQL;

namespace OpenSleigh.E2ETests.CosmosSQLServiceBus
{
    public class CosmosSqlSimpleSagaScenario : SimpleSagaScenario, 
        IClassFixture<DbFixture>, 
        IClassFixture<ServiceBusFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _cosmosFixture;
        private readonly ServiceBusFixture _sbFixture;

        private readonly string _topicName;
        private readonly string _subscriptionName;

        public CosmosSqlSimpleSagaScenario(DbFixture cosmosFixture, ServiceBusFixture sbFixture)
        {
            _cosmosFixture = cosmosFixture;
            _sbFixture = sbFixture;

            var messageName = nameof(StartSimpleSaga).ToLower();
            _topicName = $"{messageName}.tests.{Guid.NewGuid()}";
            _subscriptionName = $"{messageName}.workers";            
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var sqlCfg = new CosmosSqlConfiguration(_cosmosFixture.ConnectionString, _cosmosFixture.DbName);

            cfg.UseAzureServiceBusTransport(_sbFixture.Configuration, builder =>
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
            var adminClient = new ServiceBusAdministrationClient(_sbFixture.Configuration.ConnectionString);
            await adminClient.DeleteTopicAsync(_topicName);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
