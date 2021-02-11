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
        
        private readonly Dictionary<Type, string> _topics = new();

        public ServiceBusParentChildScenario(ServiceBusFixture fixture)
        {
            _fixture = fixture;

            AddTopicName<StartParentSaga>();
            AddTopicName<ProcessParentSaga>();
            AddTopicName<ParentSagaCompleted>();
            AddTopicName<StartChildSaga>();
            AddTopicName<ProcessChildSaga>();
            AddTopicName<ChildSagaCompleted>();
        }

        private void AddTopicName<T>() =>
            _topics[typeof(T)] = Guid.NewGuid().ToString();

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            cfg.UseAzureServiceBusTransport(_fixture.Configuration, builder =>
                {
                    builder.UseMessageNamingPolicy<StartParentSaga>(() =>
                        new QueueReferences(_topics[typeof(StartParentSaga)], _topics[typeof(StartParentSaga)]));
                    builder.UseMessageNamingPolicy<ProcessParentSaga>(() =>
                        new QueueReferences(_topics[typeof(ProcessParentSaga)], _topics[typeof(ProcessParentSaga)]));
                    builder.UseMessageNamingPolicy<ParentSagaCompleted>(() =>
                        new QueueReferences(_topics[typeof(ParentSagaCompleted)], _topics[typeof(ParentSagaCompleted)]));
                    builder.UseMessageNamingPolicy<StartChildSaga>(() =>
                        new QueueReferences(_topics[typeof(StartChildSaga)], _topics[typeof(StartChildSaga)]));
                    builder.UseMessageNamingPolicy<ProcessChildSaga>(() =>
                        new QueueReferences(_topics[typeof(ProcessChildSaga)], _topics[typeof(ProcessChildSaga)]));
                    builder.UseMessageNamingPolicy<ChildSagaCompleted>(() =>
                        new QueueReferences(_topics[typeof(ChildSagaCompleted)], _topics[typeof(ChildSagaCompleted)]));
                })
                .UseInMemoryPersistence();
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_fixture.Configuration.ConnectionString);
            foreach(var topicName in _topics.Values)
                await adminClient.DeleteTopicAsync(topicName);
        }
    }
}
