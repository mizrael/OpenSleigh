using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQLServer;
using OpenSleigh.Persistence.SQLServer.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using RabbitMQ.Client;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.SQLRabbit
{
    public class SqlServerSimpleSagaScenario : SimpleSagaScenario, 
        IClassFixture<DbFixture>,
        IClassFixture<RabbitFixture>,
        IAsyncLifetime
    {
        private readonly DbFixture _dbFixture;
        private readonly RabbitFixture _rabbitFixture;
        private readonly string _exchangeName;

        public SqlServerSimpleSagaScenario(DbFixture dbFixture, RabbitFixture rabbitFixture)
        {
            _dbFixture = dbFixture;
            _rabbitFixture = rabbitFixture;
            _exchangeName = $"test.{nameof(StartSimpleSaga)}.{Guid.NewGuid()}";
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var (_, connStr) = _dbFixture.CreateDbContext();
            var sqlCfg = new SqlConfiguration(connStr);

            cfg.UseRabbitMQTransport(_rabbitFixture.RabbitConfiguration, builder => {
                builder.UseMessageNamingPolicy<StartSimpleSaga>(() =>
                new QueueReferences(_exchangeName,
                                    _exchangeName + ".workers",
                                    nameof(StartSimpleSaga),
                                    $"{_exchangeName}.dead",
                                    $"{_exchangeName}.dead"));
            })
                .UseSqlServerPersistence(sqlCfg);
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
