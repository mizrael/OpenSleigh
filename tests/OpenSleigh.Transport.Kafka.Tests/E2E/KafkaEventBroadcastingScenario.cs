using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.E2E
{
    public class KafkaEventBroadcastingScenario : EventBroadcastingScenario, IClassFixture<KafkaFixture>
    {
        private readonly KafkaFixture _fixture;

        public KafkaEventBroadcastingScenario(KafkaFixture fixture)
        {
            _fixture = fixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            cfg.UseKafkaTransport(_fixture.KafkaConfiguration)
                .UseInMemoryPersistence();
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseKafkaTransport();
    }
}