using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using RabbitMQ.Client;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.SQLRabbit
{
    public class SqlEventBroadcastingScenario : EventBroadcastingScenario,
        IClassFixture<RabbitFixture>, 
        IClassFixture<DbFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _fixture;
        private readonly RabbitFixture _rabbitFixture;
        private readonly string _exchangeName;

        public SqlEventBroadcastingScenario(DbFixture fixture, RabbitFixture rabbitFixture)
        {
            _fixture = fixture;
            _exchangeName = $"test.{nameof(DummyEvent)}.{Guid.NewGuid()}";
            _rabbitFixture = rabbitFixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var sqlCfg = new SqlConfiguration(_fixture.ConnectionString);

            cfg.UseRabbitMQTransport(_rabbitFixture.RabbitConfiguration, builder => {
                builder.UseMessageNamingPolicy<DummyEvent>(() =>
                    new QueueReferences(_exchangeName,
                                        _exchangeName,
                                        $"{_exchangeName}.dead",
                                        $"{_exchangeName}.dead"));
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
            channel.ExchangeDelete(_exchangeName);
            channel.QueueDelete(_exchangeName);
            channel.ExchangeDelete($"{_exchangeName}.dead");
            channel.QueueDelete($"{_exchangeName}.dead");
        }
    }
}