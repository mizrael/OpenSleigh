using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    [ExcludeFromCodeCoverage]
    public static class KafkaMessageConfiguratorExtensions
    {
        public static IMessageHandlerConfigurator<TM> UseKafkaTransport<TM>(this IMessageHandlerConfigurator<TM> configurator)
            where TM : IMessage
        {
            var messageType = typeof(TM);
            
            configurator.Services.AddBusSubscriber(
                typeof(KafkaSubscriber<>).MakeGenericType(messageType));

            configurator.Services.AddSingleton(typeof(IInfrastructureCreator),
                    typeof(KafkaInfrastructureCreator<>).MakeGenericType(messageType));

            return configurator;
        }
    }
}