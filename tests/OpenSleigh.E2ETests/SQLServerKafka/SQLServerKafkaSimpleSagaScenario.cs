using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQLServer;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.SQLServerKafka
{
    public class SQLServerKafkaSimpleSagaScenario : SimpleSagaScenario, 
        IClassFixture<KafkaFixture>,
        IClassFixture<Persistence.SQLServer.Tests.Fixtures.DbFixture>,
        IAsyncLifetime
    {
        private readonly KafkaFixture _kafkaFixture;
        private readonly Persistence.SQLServer.Tests.Fixtures.DbFixture _dbFixture;
        private readonly string _topicName;
        private readonly SqlConfiguration _sqlConfig;

        public SQLServerKafkaSimpleSagaScenario(KafkaFixture kafkaFixture, Persistence.SQLServer.Tests.Fixtures.DbFixture dbFixture)
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
                .UseSqlServerPersistence(_sqlConfig);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseKafkaTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
