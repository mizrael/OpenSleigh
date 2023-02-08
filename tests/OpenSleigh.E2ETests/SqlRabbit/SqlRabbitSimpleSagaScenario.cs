using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQLServer;
using OpenSleigh.Persistence.SQLServer.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;

namespace OpenSleigh.E2ETests.SqlRabbit
{
    public class SqlRabbitSimpleSagaScenario : 
        SimpleSagaScenario,
        IClassFixture<DbFixture>,
        IClassFixture<RabbitFixture>
    {
        private readonly RabbitFixture _rabbitFixture;        
        private readonly DbFixture _dbFixture;

        public SqlRabbitSimpleSagaScenario(DbFixture dbFixture, RabbitFixture rabbitFixture)
        {
            _dbFixture = dbFixture;
            _rabbitFixture = rabbitFixture;     
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var (_, connStr) = _dbFixture.CreateDbContext();
            var sqlCfg = new SqlConfiguration(connStr);
            
            cfg.UseSqlServerPersistence(sqlCfg)
                .UseRabbitMQTransport(_rabbitFixture.RabbitConfiguration);
        }
    }
}