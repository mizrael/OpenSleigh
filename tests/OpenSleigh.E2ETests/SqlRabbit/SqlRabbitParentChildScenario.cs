using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;

namespace OpenSleigh.E2ETests.SqlRabbit
{
    public class SqlRabbitParentChildScenario : 
        ParentChildScenario,
        IClassFixture<SqlServerDbFixture>,
        IClassFixture<RabbitFixture>
    {
        private readonly RabbitFixture _rabbitFixture;
        private readonly DbFixture _dbFixture;
        private readonly string _exchangeName;

        public SqlRabbitParentChildScenario(SqlServerDbFixture dbFixture, RabbitFixture rabbitFixture)
        {
            _dbFixture = dbFixture;
            _rabbitFixture = rabbitFixture;
            _exchangeName = Guid.NewGuid().ToString();
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
            => SqlRabbitScenarioUtils.ConfigureTransportAndPersistence(cfg, _dbFixture, _rabbitFixture, _exchangeName);
    }
}