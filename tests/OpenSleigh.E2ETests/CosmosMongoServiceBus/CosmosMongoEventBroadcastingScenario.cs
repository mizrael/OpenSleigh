using Azure.Messaging.ServiceBus.Administration;
using MongoDB.Driver;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.Cosmos.Mongo.Tests.Fixtures;
using OpenSleigh.Transport.AzureServiceBus;
using OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures;
using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.Mongo.Tests.E2E
{
    public class CosmosMongoEventBroadcastingScenario : EventBroadcastingScenario, 
        IClassFixture<DbFixture>, 
        IClassFixture<ServiceBusFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _cosmosFixture;
        private readonly ServiceBusFixture _sbFixture;
        private readonly string _topicName;
        private readonly string _subscriptionName;

        public CosmosMongoEventBroadcastingScenario(DbFixture cosmosFixture, ServiceBusFixture sbFixture)
        {
            _cosmosFixture = cosmosFixture;
            _topicName = $"ServiceBusEventBroadcastingScenario.tests.{Guid.NewGuid()}";
            _subscriptionName = Guid.NewGuid().ToString();
            _sbFixture = sbFixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var cosmosCfg = new CosmosConfiguration(_cosmosFixture.ConnectionString,
                _cosmosFixture.DbName,
                CosmosSagaStateRepositoryOptions.Default,
                CosmosOutboxRepositoryOptions.Default);

            cfg.UseAzureServiceBusTransport(_sbFixture.Configuration, builder =>
            {
                QueueReferencesPolicy<DummyEvent> policy = () => new QueueReferences(_topicName, _subscriptionName);
                builder.UseMessageNamingPolicy(policy);
            })
                .UseCosmosPersistence(cosmosCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_sbFixture.Configuration.ConnectionString);

            await adminClient.DeleteSubscriptionAsync(_topicName, _subscriptionName);
            await adminClient.DeleteTopicAsync(_topicName);

            var settings = MongoClientSettings.FromUrl(new MongoUrl(_cosmosFixture.ConnectionString));
            settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            var mongoClient = new MongoClient(settings);
            await mongoClient.DropDatabaseAsync(_cosmosFixture.DbName);
        }
    }
}