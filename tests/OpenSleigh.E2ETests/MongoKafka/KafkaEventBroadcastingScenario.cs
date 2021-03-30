using System;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.MongoKafka
{
    public class KafkaEventBroadcastingScenario : EventBroadcastingScenario,
        IClassFixture<KafkaFixture>,
        IClassFixture<Persistence.Mongo.Tests.Fixtures.DbFixture>,
        IAsyncLifetime
    {
        private readonly KafkaFixture _kafkaFixture;
        private readonly Persistence.Mongo.Tests.Fixtures.DbFixture _mongoFixture;
        private readonly string _topicPrefix = $"KafkaEventBroadcastingScenario.{Guid.NewGuid()}";
        public KafkaEventBroadcastingScenario(KafkaFixture kafkaFixture, Persistence.Mongo.Tests.Fixtures.DbFixture mongoFixture)
        {
            _kafkaFixture = kafkaFixture;
            _mongoFixture = mongoFixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            var (_, name) = _mongoFixture.CreateDbContext();
            var mongoCfg = new MongoConfiguration(_mongoFixture.ConnectionString,
                name,
                MongoSagaStateRepositoryOptions.Default,
                MongoOutboxRepositoryOptions.Default);

            var kafkaConfig = _kafkaFixture.BuildKafkaConfiguration(_topicPrefix);
            cfg.UseKafkaTransport(kafkaConfig)
                .UseMongoPersistence(mongoCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseKafkaTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;
    }
}