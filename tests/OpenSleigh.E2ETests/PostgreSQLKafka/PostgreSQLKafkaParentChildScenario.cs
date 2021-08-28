using System;
using System.Threading.Tasks;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;
using OpenSleigh.Persistence.PostgreSQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.E2ETests.PostgresSQLKafka
{
    public class PostgreSQLKafkaParentChildScenario : ParentChildScenario, IClassFixture<KafkaFixture>,
        IClassFixture<DbFixture>,
        IAsyncLifetime
    {
        private readonly KafkaFixture _kafkaFixture;
        private readonly DbFixture _dbFixture;
        
        private readonly string _topicName = $"KafkaParentChildScenario.{Guid.NewGuid()}";

        private readonly SqlConfiguration _sqlConfig;

        public PostgreSQLKafkaParentChildScenario(KafkaFixture kafkaFixture, DbFixture dbFixture)
        {
            _kafkaFixture = kafkaFixture;
            _dbFixture = dbFixture;
            _topicName = $"SQLKafkaSimpleSagaScenario.{Guid.NewGuid()}";
            
            var (_, connStr) = _dbFixture.CreateDbContext();
            _sqlConfig = new SqlConfiguration(connStr);
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var kafkaConfig = _kafkaFixture.BuildKafkaConfiguration(_topicName);
            cfg.UseKafkaTransport(kafkaConfig)
                .UsePostgreSqlPersistence(_sqlConfig);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseKafkaTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
