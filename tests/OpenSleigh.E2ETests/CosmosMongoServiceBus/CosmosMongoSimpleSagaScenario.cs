using Azure.Messaging.ServiceBus.Administration;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.Cosmos.Mongo.Tests.Fixtures;
using OpenSleigh.Transport.AzureServiceBus;
using OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.Mongo.Tests.E2E
{
    public class CosmosMongoSimpleSagaScenario : SimpleSagaScenario, IClassFixture<DbFixture>, 
        IClassFixture<ServiceBusFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _cosmosFixture;
        private readonly ServiceBusFixture _sbFixture;
        private readonly string _topicName;
        private readonly string _subscriptionName;

        public CosmosMongoSimpleSagaScenario(DbFixture fixture, ServiceBusFixture sbFixture)
        {
            _cosmosFixture = fixture;

            var messageName = nameof(StartSimpleSaga).ToLower();
            _topicName = $"{messageName}.tests.{Guid.NewGuid()}";
            _subscriptionName = $"{messageName}.workers";
            _sbFixture = sbFixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var mongoCfg = new CosmosConfiguration(_cosmosFixture.ConnectionString,
                _cosmosFixture.DbName,
                CosmosSagaStateRepositoryOptions.Default,
                CosmosOutboxRepositoryOptions.Default);

            cfg.UseAzureServiceBusTransport(_sbFixture.Configuration, builder =>
            {
                builder.UseMessageNamingPolicy<StartSimpleSaga>(() =>
                    new QueueReferences(_topicName, _subscriptionName));
            })
                .UseCosmosPersistence(mongoCfg);
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
