﻿using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using Xunit;
using OpenSleigh.Transport.AzureServiceBus;
using System.Collections.Generic;
using System;
using OpenSleigh.Core.Tests.Sagas;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures;
using OpenSleigh.Persistence.Cosmos.SQL;

namespace OpenSleigh.E2ETests.CosmosSQLServiceBus
{
    public class CosmosSqlParentChildScenario : ParentChildScenario, 
        IClassFixture<DbFixture>, 
        IClassFixture<ServiceBusFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _cosmosFixture;
        private readonly ServiceBusFixture _sbFixture;
        private readonly Dictionary<Type, string> _topics = new();

        public CosmosSqlParentChildScenario(DbFixture cosmosFixture, ServiceBusFixture sbFixture)
        {
            _cosmosFixture = cosmosFixture;
            _sbFixture = sbFixture;

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
            var (_, dbName) = _cosmosFixture.CreateDbContext();
            var sqlCfg = new CosmosSqlConfiguration(_cosmosFixture.ConnectionString, dbName);

            cfg.UseAzureServiceBusTransport(_sbFixture.Configuration, builder =>
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
                .UseCosmosSqlPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var adminClient = new ServiceBusAdministrationClient(_sbFixture.Configuration.ConnectionString);
            foreach (var topicName in _topics.Values)
                await adminClient.DeleteTopicAsync(topicName);
        }
    }

}
