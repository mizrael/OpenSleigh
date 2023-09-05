using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using RabbitMQ.Client;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport.RabbitMQ
{
    [ExcludeFromCodeCoverage]
    public static class IBusConfiguratorExtensions
    {
        public static IBusConfigurator UseRabbitMQTransport(this IBusConfigurator busConfigurator,
            RabbitConfiguration config)
        {
            busConfigurator.Services.AddSingleton<IQueueReferenceFactory, QueueReferenceFactory>();            
            busConfigurator.Services.AddSingleton<IPublisher, RabbitPublisher>();
            busConfigurator.Services.AddSingleton<IChannelFactory, ChannelFactory>();

            busConfigurator.Services.AddSingleton<IConnectionFactory>(ctx =>
            {
                var connectionFactory = new ConnectionFactory()
                {
                    HostName = config.HostName,
                    VirtualHost = config.VirtualHost,
                    UserName = config.UserName,
                    Password = config.Password,
                    Port = AmqpTcpEndpoint.UseDefaultPort,
                    DispatchConsumersAsync = true
                };
                return connectionFactory;
            });

            busConfigurator.Services.AddSingleton<IBusConnection, RabbitPersistentConnection>();
            busConfigurator.Services.AddSingleton(typeof(IMessageSubscriber<>), typeof(RabbitMessageSubscriber<>));

            busConfigurator.Services.AddSingleton(config);

            return busConfigurator;
        }
    }
}