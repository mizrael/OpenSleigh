using System;
using System.Collections.Generic;
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
    public class ServiceBusParentChildScenario : ParentChildScenario, IClassFixture<ServiceBusFixture>, IAsyncLifetime
    {
        private readonly ServiceBusFixture _fixture;
        private readonly string _topicName;
        private readonly Dictionary<Type, string> _subscriptions = new();

        public ServiceBusParentChildScenario(ServiceBusFixture fixture)
        {
            _fixture = fixture;

            _topicName = $"ServiceBusParentChildScenario.tests.{Guid.NewGuid()}";

            _subscriptions[typeof(StartParentSaga)] = Guid.NewGuid().ToString();
            _subscriptions[typeof(ProcessParentSaga)] = Guid.NewGuid().ToString();
            _subscriptions[typeof(ParentSagaCompleted)] = Guid.NewGuid().ToString();
            _subscriptions[typeof(StartChildSaga)] = Guid.NewGuid().ToString();
            _subscriptions[typeof(ProcessChildSaga)] = Guid.NewGuid().ToString();
            _subscriptions[typeof(ChildSagaCompleted)] = Guid.NewGuid().ToString();
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            cfg.UseAzureServiceBusTransport(_fixture.Configuration, builder =>
                {
                    builder.UseMessageNamingPolicy<StartParentSaga>(() =>
                        new QueueReferences(_topicName, _subscriptions[typeof(StartParentSaga)]));
                    builder.UseMessageNamingPolicy<ProcessParentSaga>(() =>
                        new QueueReferences(_topicName, _subscriptions[typeof(ProcessParentSaga)]));
                    builder.UseMessageNamingPolicy<ParentSagaCompleted>(() =>
                        new QueueReferences(_topicName, _subscriptions[typeof(ParentSagaCompleted)]));
                    builder.UseMessageNamingPolicy<StartChildSaga>(() =>
                        new QueueReferences(_topicName, _subscriptions[typeof(StartChildSaga)]));
                    builder.UseMessageNamingPolicy<ProcessChildSaga>(() =>
                        new QueueReferences(_topicName, _subscriptions[typeof(ProcessChildSaga)]));
                    builder.UseMessageNamingPolicy<ChildSagaCompleted>(() =>
                        new QueueReferences(_topicName, _subscriptions[typeof(ChildSagaCompleted)]));
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

            foreach (var val in _subscriptions.Values)
                if (!(await adminClient.SubscriptionExistsAsync(_topicName, val)))
                    await adminClient.CreateSubscriptionAsync(_topicName, val);
        }

        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_fixture.Configuration.ConnectionString);
            foreach (var val in _subscriptions.Values)
                await adminClient.DeleteSubscriptionAsync(_topicName, val);
            await adminClient.DeleteTopicAsync(_topicName);
        }
    }
}
