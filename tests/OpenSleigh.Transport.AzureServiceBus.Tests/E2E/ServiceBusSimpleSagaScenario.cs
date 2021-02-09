using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Hosting;
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
                    QueueReferencesPolicy<StartSimpleSaga> policy = () => new QueueReferences(_topicName, _subscriptionName);
                    builder.UseMessageNamingPolicy(policy);
                })
                .UseInMemoryPersistence();
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();

        public async Task InitializeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_fixture.Configuration.ConnectionString);

            if (!(await adminClient.TopicExistsAsync(_topicName)))
                await adminClient.CreateTopicAsync(_topicName);

            if (!(await adminClient.SubscriptionExistsAsync(_topicName, _subscriptionName)))
                await adminClient.CreateSubscriptionAsync(_topicName, _subscriptionName);
        }

        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_fixture.Configuration.ConnectionString);
            await adminClient.DeleteSubscriptionAsync(_topicName, _subscriptionName);
            await adminClient.DeleteTopicAsync(_topicName);
        }
    }
}
