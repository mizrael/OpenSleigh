using Azure.Messaging.ServiceBus.Administration;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQLServer;
using OpenSleigh.Persistence.SQLServer.Tests.Fixtures;
using OpenSleigh.Transport.AzureServiceBus;
using OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.SQLServerServiceBus
{
    public class SQLServerServiceBusSimpleSagaScenario : SimpleSagaScenario, 
        IClassFixture<DbFixture>,
        IClassFixture<ServiceBusFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _dbFixture;
        private readonly ServiceBusFixture _sbFixture;
        
        private readonly string _topicName;
        private readonly string _subscriptionName;

        public SQLServerServiceBusSimpleSagaScenario(DbFixture dbFixture, ServiceBusFixture sbFixture)
        {
            _dbFixture = dbFixture;
            _sbFixture = sbFixture;

            var messageName = nameof(StartSimpleSaga).ToLower();
            _topicName = $"{messageName}.tests.{Guid.NewGuid()}";
            _subscriptionName = $"{messageName}.workers";
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var (_, connStr) = _dbFixture.CreateDbContext();
            var sqlCfg = new SqlConfiguration(connStr);

            cfg.UseAzureServiceBusTransport(_sbFixture.Configuration, builder =>
            {
                builder.UseMessageNamingPolicy<StartSimpleSaga>(() =>
                    new Transport.AzureServiceBus.QueueReferences(_topicName, _subscriptionName));
            })
                .UseSqlServerPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();

        public Task InitializeAsync() => Task.CompletedTask;
                
        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_sbFixture.Configuration.ConnectionString);
            await adminClient.DeleteTopicAsync(_topicName);
        }
    }
}
