using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.E2E
{
    public class RabbitSimpleSagaScenario : SimpleSagaScenario, IClassFixture<RabbitFixture>
    {
        private readonly RabbitFixture _fixture;

        public RabbitSimpleSagaScenario(RabbitFixture fixture)
        {
            _fixture = fixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            cfg.UseRabbitMQTransport(_fixture.RabbitConfiguration)
                .UseInMemoryPersistence();
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseRabbitMQTransport();
    }
}
