using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Transport.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using System.Linq;

namespace OpenSleigh.Persistence.InMemory
{
    public record RabbitConfiguration(string HostName, string UserName, string Password);

    public static class RabbitMQSagaConfiguratorExtensions
    {
        private static bool _initialized = false;

        public static ISagaConfigurator<TS, TD> UseRabbitMQTransport<TS, TD>(this ISagaConfigurator<TS, TD> sagaConfigurator,
            RabbitConfiguration config)
            where TS : Saga<TD>
            where TD : SagaState
        {
            var sagaType = typeof(TS);
            var messageHandlerType = typeof(IHandleMessage<>).GetGenericTypeDefinition();
            var interfaces = sagaType.GetInterfaces();
            foreach (var i in interfaces)
            {
                if (!i.IsGenericType)
                    continue;

                var openGeneric = i.GetGenericTypeDefinition();
                if (!openGeneric.IsAssignableFrom(messageHandlerType))
                    continue;

                var messageType = i.GetGenericArguments().First();
                
                sagaConfigurator.Services.AddSingleton(typeof(ISubscriber<>).MakeGenericType(messageType),
                                                       typeof(RabbitSubscriber<>).MakeGenericType(messageType));
            }

            if (!_initialized)
            {
                var encoder = new JsonEncoder();
                sagaConfigurator.Services.AddSingleton<IEncoder>(encoder);
                sagaConfigurator.Services.AddSingleton<IDecoder>(encoder);

                sagaConfigurator.Services.AddSingleton<IQueueReferenceFactory, QueueReferenceFactory>();
                sagaConfigurator.Services.AddSingleton<IMessageParser, MessageParser>();
                sagaConfigurator.Services.AddSingleton<IPublisher, RabbitPublisher>();
                sagaConfigurator.Services.AddSingleton<IPublisherChannelFactory, PublisherChannelFactory>();
                
                sagaConfigurator.Services.AddSingleton<IConnectionFactory>(ctx =>
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

                sagaConfigurator.Services.AddSingleton<IBusConnection, RabbitPersistentConnection>();

                _initialized = true;
            }

            return sagaConfigurator;
        }
    }
}