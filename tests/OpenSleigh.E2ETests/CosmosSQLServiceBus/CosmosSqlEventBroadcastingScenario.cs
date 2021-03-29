using Azure.Messaging.ServiceBus.Administration;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.Cosmos.SQL;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using OpenSleigh.Transport.AzureServiceBus;
using OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using Xunit;

namespace OpenSleigh.E2ETests.CosmosSQLServiceBus
{
    public class CosmosSqlEventBroadcastingScenario : EventBroadcastingScenario, 
        IClassFixture<DbFixture>, 
        IClassFixture<ServiceBusFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _cosmosFixture;
        private readonly ServiceBusFixture _sbFixture;
        private readonly string _topicName;

        public CosmosSqlEventBroadcastingScenario(DbFixture cosmosFixture, ServiceBusFixture sbFixture)
        {
            _cosmosFixture = cosmosFixture;
            _sbFixture = sbFixture; 
            
            _topicName =  $"{nameof(DummyEvent)}.{DateTime.UtcNow.Ticks}";
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var (_, dbName) = _cosmosFixture.CreateDbContext();
            var sqlCfg = new CosmosSqlConfiguration(_cosmosFixture.ConnectionString, dbName);

            cfg.UseAzureServiceBusTransport(_sbFixture.Configuration, builder =>
            {
                QueueReferencesPolicy<DummyEvent> policy = () =>
                {
                    var sp = cfg.Services.BuildServiceProvider();
                    var sysInfo = sp.GetService<SystemInfo>();
                    var subscriptionName = sysInfo.ClientGroup;
                    return new QueueReferences(_topicName, subscriptionName);
                };
                builder.UseMessageNamingPolicy(policy);
            })
                .UseCosmosSqlPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_sbFixture.Configuration.ConnectionString);

            await foreach (var subscription in adminClient.GetSubscriptionsAsync(_topicName))
            {
                await adminClient.DeleteSubscriptionAsync(_topicName, subscription.SubscriptionName);    
            }
            
            await adminClient.DeleteTopicAsync(_topicName);
        }
    }
}