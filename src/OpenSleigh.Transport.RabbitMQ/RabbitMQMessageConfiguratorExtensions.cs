using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    [ExcludeFromCodeCoverage]
    public static class RabbitMQMessageConfiguratorExtensions
    {
        public static IMessageHandlerConfigurator<TM> UseRabbitMQTransport<TM>(this IMessageHandlerConfigurator<TM> configurator)
            where TM : IMessage
        {
            var messageType = typeof(TM);
            configurator.Services.AddBusSubscriber(
                typeof(RabbitSubscriber<>).MakeGenericType(messageType));
            return configurator;
        }
    }
}