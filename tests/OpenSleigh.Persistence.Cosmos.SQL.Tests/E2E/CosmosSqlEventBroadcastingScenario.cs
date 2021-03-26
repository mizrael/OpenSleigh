using Azure.Messaging.ServiceBus.Administration;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using OpenSleigh.Transport.AzureServiceBus;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.E2E
{
    public class CosmosSqlEventBroadcastingScenario : EventBroadcastingScenario, IClassFixture<DbFixture>, IAsyncLifetime
    {
        private readonly DbFixture _fixture;
        private readonly string _topicName;
        private readonly string _subscriptionName;

        public CosmosSqlEventBroadcastingScenario(DbFixture fixture)
        {
            _fixture = fixture;
            _topicName = $"ServiceBusEventBroadcastingScenario.tests.{Guid.NewGuid()}";
            _subscriptionName = Guid.NewGuid().ToString();
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var sqlCfg = new CosmosSqlConfiguration(_fixture.ConnectionString, _fixture.DbName);

            cfg.UseAzureServiceBusTransport(_fixture.AzureServiceBusConfiguration, builder =>
            {
                QueueReferencesPolicy<DummyEvent> policy = () => new QueueReferences(_topicName, _subscriptionName);
                builder.UseMessageNamingPolicy(policy);
            })
                .UseCosmosSqlPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_fixture.AzureServiceBusConfiguration.ConnectionString);

            await adminClient.DeleteSubscriptionAsync(_topicName, _subscriptionName);
            await adminClient.DeleteTopicAsync(_topicName);
        }
    }
}