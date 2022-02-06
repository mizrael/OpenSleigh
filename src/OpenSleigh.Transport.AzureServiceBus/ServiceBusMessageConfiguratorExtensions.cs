using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    [ExcludeFromCodeCoverage]
    public static class ServiceBusMessageConfiguratorExtensions
    {
        public static IMessageHandlerConfigurator<TM> UseAzureServiceBusTransport<TM>(this IMessageHandlerConfigurator<TM> configurator)
            where TM : IMessage
        {
            var messageType = typeof(TM);

            configurator.Services.AddBusSubscriber(
                typeof(ServiceBusSubscriber<>).MakeGenericType(messageType));

            configurator.Services.AddSingleton(typeof(IInfrastructureCreator),
                    typeof(AzureServiceBusInfrastructureCreator<>).MakeGenericType(messageType));

            return configurator;
        }
    }
}