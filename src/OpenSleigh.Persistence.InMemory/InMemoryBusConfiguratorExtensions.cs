using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Persistence.InMemory.Messaging;

namespace OpenSleigh.Persistence.InMemory
{
    [ExcludeFromCodeCoverage]
    public static class InMemoryBusConfiguratorExtensions
    {
        public static IBusConfigurator UseInMemoryPersistence(
            this IBusConfigurator busConfigurator)
        {
            busConfigurator.Services.AddSingleton<ISagaStateRepository, InMemorySagaStateRepository>()
                                    .AddSingleton<IOutboxRepository, InMemoryOutboxRepository>()
                                    .AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();

            return busConfigurator;
        }

        public static IBusConfigurator UseInMemoryTransport(
            this IBusConfigurator busConfigurator)
        {
            busConfigurator.Services.AddSingleton<IPublisher, InMemoryPublisher>()
                .AddSingleton<IChannelFactory, ChannelFactory>();

            return busConfigurator;
        }
    }
}