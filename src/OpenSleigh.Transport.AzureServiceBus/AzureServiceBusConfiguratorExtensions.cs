using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using Azure.Messaging.ServiceBus;

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
            //https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-performance-improvements?tabs=net-standard-sdk-2#reusing-factories-and-clients
            busConfigurator.Services
                .AddSingleton(config)
                .AddSingleton(ctx => new ServiceBusClient(config.ConnectionString, new ServiceBusClientOptions()))
                .AddSingleton<IQueueReferenceFactory, QueueReferenceFactory>()
                .AddSingleton<IServiceBusSenderFactory, ServiceBusSenderFactory>()     
                .AddSingleton<IPublisher, ServiceBusPublisher>();

            builderFunc?.Invoke(new DefaultAzureServiceBusConfigurationBuilder(busConfigurator));

            return busConfigurator;
        }
    }
}