using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQLServer;
using OpenSleigh.Persistence.SQLServer.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;

namespace OpenSleigh.E2ETests.SqlRabbit
{
    public class SqlRabbitParentChildScenario : 
        ParentChildScenario,
        IClassFixture<DbFixture>,
        IClassFixture<RabbitFixture>
    {
        private readonly RabbitFixture _rabbitFixture;
        private readonly DbFixture _dbFixture;
        private readonly string _exchangeName;

        public SqlRabbitParentChildScenario(DbFixture dbFixture, RabbitFixture rabbitFixture)
        {
            _dbFixture = dbFixture;
            _rabbitFixture = rabbitFixture;
            _exchangeName = Guid.NewGuid().ToString();
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var (_, connStr) = _dbFixture.CreateDbContext();
            var sqlCfg = new SqlConfiguration(connStr);

            QueueReferencesCreator creator = messageType =>
            {
                var queueName = $"{_exchangeName}.{messageType.Name}.workers";
                var dlExchangeName = _exchangeName + ".dead";
                var dlQueueName = $"{dlExchangeName}.{messageType.Name}.workers";
                return new QueueReferences(_exchangeName, queueName, dlExchangeName, dlQueueName);
            };
            cfg.Services.AddSingleton(creator);

            cfg.UseSqlServerPersistence(sqlCfg)
                .UseRabbitMQTransport(_rabbitFixture.RabbitConfiguration);
        }
    }
}