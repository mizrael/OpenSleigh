using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using Microsoft.Extensions.Azure;

namespace OpenSleigh.Transport.AzureServiceBus
{
    [ExcludeFromCodeCoverage]
    public record AzureServiceBusConfiguration(string ConnectionString);

    [ExcludeFromCodeCoverage]
    public static class AzureServiceBusConfiguratorExtensions
    {
        public static IBusConfigurator UseAzureServiceBusTransport(this IBusConfigurator busConfigurator,
            AzureServiceBusConfiguration config,
            Action<IAzureServiceBusConfigurationBuilder> builderFunc = null)
        {
            busConfigurator.Services.AddAzureClients(builder =>
            {
                builder.AddServiceBusClient(config.ConnectionString);
            });

            //TODO: evaluate programmatic topics/subscriptions/queues creation based on https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-management-libraries#azuremessagingservicebusadministration

            //https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-performance-improvements?tabs=net-standard-sdk-2#reusing-factories-and-clients
            busConfigurator.Services
                .AddSingleton<IQueueReferenceFactory, QueueReferenceFactory>()
                .AddSingleton<IServiceBusSenderFactory, ServiceBusSenderFactory>()
                .AddSingleton<IServiceBusProcessorFactory, ServiceBusProcessorFactory>()
                .AddSingleton<IMessageParser, MessageParser>()
                .AddSingleton<IPublisher, ServiceBusPublisher>();

            builderFunc?.Invoke(new DefaultAzureServiceBusConfigurationBuilder(busConfigurator));

            return busConfigurator;
        }
    }
}