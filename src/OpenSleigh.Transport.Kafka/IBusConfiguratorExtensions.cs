using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport.Kafka;

[ExcludeFromCodeCoverage]
public record KafkaConfiguration(string ConnectionString, QueueReferencesCreator? DefaultQueueReferenceCreator = null);

[ExcludeFromCodeCoverage]
public static class IBusConfiguratorExtensions
{
    public static IBusConfigurator UseKafkaTransport(this IBusConfigurator busConfigurator,
        KafkaConfiguration config)
    {
        busConfigurator.Services.AddSingleton(config);

        busConfigurator.Services.AddSingleton<IQueueReferenceFactory>(_ => new QueueReferenceFactory(config.DefaultQueueReferenceCreator));

        busConfigurator.Services.AddSingleton(ctx =>
        {
            var kafkaConfig = ctx.GetRequiredService<KafkaConfiguration>();
            return new AdminClientConfig() { BootstrapServers = kafkaConfig.ConnectionString };
        });
        busConfigurator.Services.AddSingleton(ctx =>
        {
            var adminClientConfig = ctx.GetRequiredService<AdminClientConfig>();
            return new AdminClientBuilder(adminClientConfig);
        });

        busConfigurator.Services.AddSingleton(ctx =>
        {
            var kafkaConfig = ctx.GetRequiredService<KafkaConfiguration>();
            return new ProducerConfig() { BootstrapServers = kafkaConfig.ConnectionString };
        });
        busConfigurator.Services.AddSingleton(ctx =>
        {
            var config = ctx.GetRequiredService<ProducerConfig>();
            var builder = new ProducerBuilder<string, byte[]>(config);
            builder.SetKeySerializer(new KeySerializer<string>());

            return builder;
        });
        busConfigurator.Services.AddSingleton(ctx =>
        {
            var builder = ctx.GetRequiredService<ProducerBuilder<string, byte[]>>();
            return builder.Build();
        });
        busConfigurator.Services.AddSingleton<IKafkaPublisherExecutor, KafkaPublisher>();
        busConfigurator.Services.AddSingleton<IPublisher, KafkaPublisher>();

        busConfigurator.Services.AddSingleton<IKafkaMessageParser, KafkaMessageParser>();
        // the handler is a singleton and the saga pipeline is not: it gets a scope factory
        // so that every message is processed by its own pipeline instead of one captured for the life of the process
        busConfigurator.Services.AddSingleton<IKafkaMessageHandler>(ctx => new KafkaMessageHandler(
            ctx.GetRequiredService<IKafkaMessageParser>(),
            ctx.GetRequiredService<IServiceScopeFactory>(),
            ctx.GetRequiredService<IKafkaPublisherExecutor>(),
            ctx.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KafkaMessageHandler>>(),
            ctx.GetRequiredService<ISystemInfo>(),
            ctx.GetRequiredService<IQueueReferenceFactory>()));

        busConfigurator.Services.AddSingleton<IConsumerBuilderFactory, ConsumerBuilderFactory>();

        busConfigurator.Services.AddSingleton<IMessageSubscriber, KafkaMessageSubscriber>();

        // busConfigurator.Services.AddSingleton(typeof(IInfrastructureCreator), typeof(KafkaInfrastructureCreator<>));

        return busConfigurator;
    }
}