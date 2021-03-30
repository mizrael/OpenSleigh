using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.SQLRabbit
{
    public class SqlParentChildScenario : ParentChildScenario, 
        IClassFixture<DbFixture>,
        IClassFixture<RabbitFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _dbFixture;
        private readonly RabbitFixture _rabbitFixture;
        private readonly Dictionary<Type, string> _topics = new();

        public SqlParentChildScenario(DbFixture dbFixture, RabbitFixture rabbitFixture)
        {
            _dbFixture = dbFixture;
            _rabbitFixture = rabbitFixture;
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
            var (_, connStr) = _dbFixture.CreateDbContext();
            var sqlCfg = new SqlConfiguration(connStr);

            cfg.UseRabbitMQTransport(_rabbitFixture.RabbitConfiguration, builder =>
            {
                builder.UseMessageNamingPolicy<StartParentSaga>(() =>
                    new QueueReferences(_topics[typeof(StartParentSaga)], _topics[typeof(StartParentSaga)],
                                        _topics[typeof(StartParentSaga)] + ".dead", _topics[typeof(StartParentSaga)] + ".dead"));

                builder.UseMessageNamingPolicy<ProcessParentSaga>(() =>
                    new QueueReferences(_topics[typeof(ProcessParentSaga)], _topics[typeof(ProcessParentSaga)],
                                        _topics[typeof(ProcessParentSaga)] + ".dead", _topics[typeof(ProcessParentSaga)] + ".dead"));

                builder.UseMessageNamingPolicy<ParentSagaCompleted>(() =>
                    new QueueReferences(_topics[typeof(ParentSagaCompleted)], _topics[typeof(ParentSagaCompleted)],
                                        _topics[typeof(ParentSagaCompleted)] + ".dead", _topics[typeof(ParentSagaCompleted)] + ".dead"));

                builder.UseMessageNamingPolicy<StartChildSaga>(() =>
                    new QueueReferences(_topics[typeof(StartChildSaga)], _topics[typeof(StartChildSaga)],
                                        _topics[typeof(StartChildSaga)] + ".dead", _topics[typeof(StartChildSaga)] + ".dead"));

                builder.UseMessageNamingPolicy<ProcessChildSaga>(() =>
                    new QueueReferences(_topics[typeof(ProcessChildSaga)], _topics[typeof(ProcessChildSaga)],
                                        _topics[typeof(ProcessChildSaga)] + ".dead", _topics[typeof(ProcessChildSaga)] + ".dead"));

                builder.UseMessageNamingPolicy<ChildSagaCompleted>(() =>
                    new QueueReferences(_topics[typeof(ChildSagaCompleted)], _topics[typeof(ChildSagaCompleted)],
                                        _topics[typeof(ChildSagaCompleted)] + ".dead", _topics[typeof(ChildSagaCompleted)] + ".dead"));
            })
                .UseSqlPersistence(sqlCfg);
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
            foreach (var kv in _topics)
            {
                channel.ExchangeDelete(kv.Value);
                channel.QueueDelete(kv.Value);

                channel.ExchangeDelete(kv.Value + ".dead");
                channel.QueueDelete(kv.Value + ".dead");
            }
        }
    }

}
