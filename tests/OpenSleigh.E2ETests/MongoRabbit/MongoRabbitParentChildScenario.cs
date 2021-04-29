using MongoDB.Driver;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.MongoRabbit
{
    public class MongoRabbitParentChildScenario : ParentChildScenario,
        IClassFixture<RabbitFixture>,
        IClassFixture<Persistence.Mongo.Tests.Fixtures.DbFixture>,
        IAsyncLifetime
    {
        private readonly RabbitFixture _rabbitFixture;
        private readonly Persistence.Mongo.Tests.Fixtures.DbFixture _mongoFixture;
        private readonly Dictionary<Type, string> _topics = new();
        private readonly List<string> _dbNames = new();

        public MongoRabbitParentChildScenario(RabbitFixture fixture, Persistence.Mongo.Tests.Fixtures.DbFixture mongoFixture)
        {
            _rabbitFixture = fixture;
            _mongoFixture = mongoFixture;

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
            var (_, name) = _mongoFixture.CreateDbContext();
            _dbNames.Add(name);
            var mongoCfg = new MongoConfiguration(_mongoFixture.ConnectionString,
                name,
                MongoSagaStateRepositoryOptions.Default,
                MongoOutboxRepositoryOptions.Default);

            cfg.UseRabbitMQTransport(_rabbitFixture.RabbitConfiguration, builder =>
            {
                builder.UseMessageNamingPolicy<StartParentSaga>(() =>
                        new QueueReferences(_topics[typeof(StartParentSaga)], _topics[typeof(StartParentSaga)], _topics[typeof(StartParentSaga)],
                                           _topics[typeof(StartParentSaga)] + ".dead", _topics[typeof(StartParentSaga)] + ".dead"));

                builder.UseMessageNamingPolicy<ProcessParentSaga>(() =>
                    new QueueReferences(_topics[typeof(ProcessParentSaga)], _topics[typeof(ProcessParentSaga)], _topics[typeof(ProcessParentSaga)],
                                        _topics[typeof(ProcessParentSaga)] + ".dead", _topics[typeof(ProcessParentSaga)] + ".dead"));

                builder.UseMessageNamingPolicy<ParentSagaCompleted>(() =>
                    new QueueReferences(_topics[typeof(ParentSagaCompleted)], _topics[typeof(ParentSagaCompleted)], nameof(ParentSagaCompleted),
                                        _topics[typeof(ParentSagaCompleted)] + ".dead", _topics[typeof(ParentSagaCompleted)] + ".dead"));

                builder.UseMessageNamingPolicy<StartChildSaga>(() =>
                    new QueueReferences(_topics[typeof(StartChildSaga)], _topics[typeof(StartChildSaga)], _topics[typeof(StartChildSaga)],
                                        _topics[typeof(StartChildSaga)] + ".dead", _topics[typeof(StartChildSaga)] + ".dead"));

                builder.UseMessageNamingPolicy<ProcessChildSaga>(() =>
                    new QueueReferences(_topics[typeof(ProcessChildSaga)], _topics[typeof(ProcessChildSaga)], _topics[typeof(ProcessChildSaga)],
                                        _topics[typeof(ProcessChildSaga)] + ".dead", _topics[typeof(ProcessChildSaga)] + ".dead"));

                builder.UseMessageNamingPolicy<ChildSagaCompleted>(() =>
                    new QueueReferences(_topics[typeof(ChildSagaCompleted)], _topics[typeof(ChildSagaCompleted)], nameof(ChildSagaCompleted),
                                        _topics[typeof(ChildSagaCompleted)] + ".dead", _topics[typeof(ChildSagaCompleted)] + ".dead"));
            })
               .UseMongoPersistence(mongoCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseRabbitMQTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var connectionFactory = new ConnectionFactory()
            {
                HostName = _rabbitFixture.RabbitConfiguration.HostName,
                UserName = _rabbitFixture.RabbitConfiguration.UserName,
                Password = _rabbitFixture.RabbitConfiguration.Password,
                Port = AmqpTcpEndpoint.UseDefaultPort,
                DispatchConsumersAsync = true
            };
            using var connection = connectionFactory.CreateConnection();
            using var channel = connection.CreateModel();
            foreach(var kv in _topics)
            {
                channel.ExchangeDelete(kv.Value);
                channel.QueueDelete(kv.Value);

                channel.ExchangeDelete(kv.Value + ".dead");
                channel.QueueDelete(kv.Value + ".dead");
            }            
        }
    }
}
