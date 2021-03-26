using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.E2ETests.MongoRabbit
{
    public class MongoRabbitParentChildScenario : ParentChildScenario,
        IClassFixture<RabbitFixture>,
        IClassFixture<Persistence.Mongo.Tests.Fixtures.DbFixture>
    {
        private readonly RabbitFixture _fixture;
        private readonly Persistence.Mongo.Tests.Fixtures.DbFixture _mongoFixture;

        public MongoRabbitParentChildScenario(RabbitFixture fixture, Persistence.Mongo.Tests.Fixtures.DbFixture mongoFixture)
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

            cfg.UseRabbitMQTransport(_fixture.RabbitConfiguration)
               .UseMongoPersistence(mongoCfg);
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseRabbitMQTransport();
    }
}
