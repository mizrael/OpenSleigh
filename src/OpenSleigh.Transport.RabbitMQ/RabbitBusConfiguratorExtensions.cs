using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    [ExcludeFromCodeCoverage]
    public record RabbitConfiguration(string HostName, string UserName, string Password);

    [ExcludeFromCodeCoverage]
    public static class RabbitBusConfiguratorExtensions
    {
        public static IBusConfigurator UseRabbitMQTransport(this IBusConfigurator busConfigurator,
            RabbitConfiguration config)
        {
            var encoder = new JsonEncoder();
            busConfigurator.Services.AddSingleton<IEncoder>(encoder);
            busConfigurator.Services.AddSingleton<IDecoder>(encoder);

            busConfigurator.Services.AddSingleton<IQueueReferenceFactory, QueueReferenceFactory>();
            busConfigurator.Services.AddSingleton<IMessageParser, MessageParser>();
            busConfigurator.Services.AddSingleton<IPublisher, RabbitPublisher>();
            busConfigurator.Services.AddSingleton<IPublisherChannelFactory, PublisherChannelFactory>();

            busConfigurator.Services.AddSingleton<IConnectionFactory>(ctx =>
            {
                var connectionFactory = new ConnectionFactory()
                {
                    HostName = config.HostName,
                    UserName = config.UserName,
                    Password = config.Password,
                    Port = AmqpTcpEndpoint.UseDefaultPort,
                    DispatchConsumersAsync = true
                };
                return connectionFactory;
            });

            busConfigurator.Services.AddSingleton<IBusConnection, RabbitPersistentConnection>();
            
            return busConfigurator;
        }
    }
}