using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.E2ETests.MongoKafka
{
    public class KafkaParentChildScenario : ParentChildScenario, IClassFixture<KafkaFixture>,
        IClassFixture<Persistence.Mongo.Tests.Fixtures.DbFixture>,
        IAsyncLifetime
    {
        private readonly KafkaFixture _fixture;
        private readonly Persistence.Mongo.Tests.Fixtures.DbFixture _mongoFixture;

        public KafkaParentChildScenario(KafkaFixture fixture, Persistence.Mongo.Tests.Fixtures.DbFixture mongoFixture)
        {
            _fixture = fixture;
            _mongoFixture = mongoFixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {            
            var mongoCfg = new MongoConfiguration(_mongoFixture.ConnectionString,
                _mongoFixture.DbName,
                MongoSagaStateRepositoryOptions.Default,
                MongoOutboxRepositoryOptions.Default);

            cfg.UseKafkaTransport(_fixture.KafkaConfiguration)
                .UseMongoPersistence(mongoCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseKafkaTransport();

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
