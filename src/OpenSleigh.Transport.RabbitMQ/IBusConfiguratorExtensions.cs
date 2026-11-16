using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSleigh.DependencyInjection;
using RabbitMQ.Client;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport.RabbitMQ;

[ExcludeFromCodeCoverage]
public static class IBusConfiguratorExtensions
{
    public static IBusConfigurator UseRabbitMQTransport(
        this IBusConfigurator busConfigurator,
        RabbitConfiguration config,
        QueueReferencesCreator? queueReferencesCreator = null)
    {
        if (queueReferencesCreator is null)
            busConfigurator.Services.TryAddSingleton(ctx =>
            {
                var sysInfo = ctx.GetRequiredService<ISystemInfo>();
                return QueueReferenceFactory.BuildDefaultCreator(sysInfo);
            });
        else
            busConfigurator.Services.AddSingleton(queueReferencesCreator);

        busConfigurator.Services.AddSingleton<IQueueReferenceFactory, QueueReferenceFactory>();
        busConfigurator.Services.AddSingleton<IPublisher, RabbitPublisher>();
        busConfigurator.Services.AddSingleton<IChannelFactory, ChannelFactory>();
        busConfigurator.Services.AddSingleton<IRabbitMessageParser, RabbitMessageParser>();

        // one instance: the publish channel hands it the messages the broker returns, and the host stops it
        busConfigurator.Services.AddSingleton<ReturnedMessageHandler>();
        busConfigurator.Services.AddSingleton<IReturnedMessageHandler>(ctx => ctx.GetRequiredService<ReturnedMessageHandler>());
        busConfigurator.Services.AddHostedService(ctx => ctx.GetRequiredService<ReturnedMessageHandler>());

        busConfigurator.Services.AddSingleton<IConnectionFactory>(ctx =>
        {
            var connectionFactory = new ConnectionFactory()
            {
                HostName = config.HostName,
                VirtualHost = config.VirtualHost,
                UserName = config.UserName,
                Password = config.Password,
                Port = AmqpTcpEndpoint.UseDefaultPort,
                NetworkRecoveryInterval = config.RetryDelay,
                AutomaticRecoveryEnabled = true,
                ConsumerDispatchConcurrency = config.ConsumerDispatchConcurrency,
            };
            return connectionFactory;
        });

        busConfigurator.Services.AddSingleton<IBusConnection, RabbitPersistentConnection>();
        busConfigurator.Services.AddSingleton<IMessageSubscriber, RabbitMessageSubscriber>();

        busConfigurator.Services.AddSingleton(config);

        return busConfigurator;
    }
}