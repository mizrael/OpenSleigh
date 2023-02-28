using Microsoft.Extensions.DependencyInjection;
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
        private readonly string _exchangeName;
        
        public SqlRabbitSimpleSagaScenario(DbFixture dbFixture, RabbitFixture rabbitFixture)
        {
            _dbFixture = dbFixture;
            _rabbitFixture = rabbitFixture;
            _exchangeName = Guid.NewGuid().ToString();
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
            => SqlRabbitScenarioUtils.ConfigureTransportAndPersistence(cfg, _dbFixture, _rabbitFixture, _exchangeName);
    }
}