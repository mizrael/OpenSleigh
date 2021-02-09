using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Tests.E2E;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Transport.AzureServiceBus;
using OpenSleigh.Transport.AzureServiceBus.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.E2E
{
    public class ServiceBusEventBroadcastingScenario : EventBroadcastingScenario, IClassFixture<ServiceBusFixture>
    {
        private readonly ServiceBusFixture _fixture;

        public ServiceBusEventBroadcastingScenario(ServiceBusFixture fixture)
        {
            _fixture = fixture;
        }

        protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        {
            cfg.UseAzureServiceBusTransport(_fixture.Configuration)
                .UseInMemoryPersistence();
        }

        protected override void ConfigureSagaTransport<TS, TD>(ISagaConfigurator<TS, TD> cfg) =>
            cfg.UseAzureServiceBusTransport();
    }
}