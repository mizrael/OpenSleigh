using System;
using System.Threading.Tasks;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.PostgreSQL;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.E2ETests.PostgresSQLKafka
{
    public class PostgreSQLKafkaEventBroadcastingScenario : EventBroadcastingScenario,
        IClassFixture<KafkaFixture>,
        IClassFixture<Persistence.SQL.PostgreSQL.Tests.Fixtures.DbFixture>,
        IAsyncLifetime
    {
        private readonly KafkaFixture _kafkaFixture;
        private readonly Persistence.SQL.PostgreSQL.Tests.Fixtures.DbFixture _dbFixture;
        private readonly string _topicPrefix = $"KafkaEventBroadcastingScenario.{Guid.NewGuid()}";
        
        public PostgreSQLKafkaEventBroadcastingScenario(KafkaFixture kafkaFixture, Persistence.SQL.PostgreSQL.Tests.Fixtures.DbFixture dbFixture)
        {
            _kafkaFixture = kafkaFixture;
            _dbFixture = dbFixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var (_, connStr) = _dbFixture.CreateDbContext();
            var sqlCfg = new SqlConfiguration(connStr);

            var kafkaConfig = _kafkaFixture.BuildKafkaConfiguration(_topicPrefix);
            cfg.UseKafkaTransport(kafkaConfig)
                .UsePostgreSqlPersistence(sqlCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseKafkaTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;
    }
}