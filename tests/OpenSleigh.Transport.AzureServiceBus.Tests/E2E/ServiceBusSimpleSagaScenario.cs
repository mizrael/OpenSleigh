using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.E2E
{
    public class ServiceBusSimpleSagaScenario : SimpleSagaScenario, 
        IClassFixture<ServiceBusFixture>,
        IAsyncLifetime
    {
        private readonly ServiceBusFixture _fixture;
        private readonly string _topicName;
        private readonly string _subscriptionName;
        
        public ServiceBusSimpleSagaScenario(ServiceBusFixture fixture)
        {
            _fixture = fixture;

            var messageName = nameof(StartSimpleSaga).ToLower();
            _topicName =  $"{messageName}.tests.{Guid.NewGuid()}";
            _subscriptionName = $"{messageName}.workers";
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            cfg.UseAzureServiceBusTransport(_fixture.Configuration, builder =>
                {
                    builder.UseMessageNamingPolicy<StartSimpleSaga>(() =>
                        new QueueReferences(_topicName, _subscriptionName));
                })
                .UseInMemoryPersistence();
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();

        public Task InitializeAsync() => Task.CompletedTask;
        
        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_fixture.Configuration.ConnectionString);
            await adminClient.DeleteTopicAsync(_topicName);
        }
    }
}
