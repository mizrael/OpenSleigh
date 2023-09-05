using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport.Kafka
{
    [ExcludeFromCodeCoverage]
    public record KafkaConfiguration(string ConnectionString, Func<Type, QueueReferences> DefaultQueueReferenceCreator = null);

    [ExcludeFromCodeCoverage]
    public static class IBusConfiguratorExtensions
    {     
        public static IBusConfigurator UseKafkaTransport(this IBusConfigurator busConfigurator,
            KafkaConfiguration config)
        {
            busConfigurator.Services.AddSingleton(config);
            
            busConfigurator.Services.AddSingleton<IQueueReferenceFactory>(ctx => new QueueReferenceFactory(ctx, config.DefaultQueueReferenceCreator));

            busConfigurator.Services.AddSingleton(ctx =>
            {
                var kafkaConfig = ctx.GetRequiredService<KafkaConfiguration>();
                return new AdminClientConfig() {BootstrapServers = kafkaConfig.ConnectionString};
            });
            busConfigurator.Services.AddSingleton(ctx =>
            {
                var adminClientConfig = ctx.GetRequiredService<AdminClientConfig>();
                return new AdminClientBuilder(adminClientConfig);
            });

            busConfigurator.Services.AddSingleton(ctx =>
            {
                var kafkaConfig = ctx.GetRequiredService<KafkaConfiguration>();
                return new ProducerConfig() {BootstrapServers = kafkaConfig.ConnectionString};
            });
            busConfigurator.Services.AddSingleton(ctx =>
            {
                var config = ctx.GetRequiredService<ProducerConfig>();
                var builder = new ProducerBuilder<string, ReadOnlyMemory<byte>>(config);
                builder.SetKeySerializer(new KeySerializer<string>());

                return builder;
            });
            busConfigurator.Services.AddSingleton(ctx =>
            {
                var builder = ctx.GetRequiredService<ProducerBuilder<Guid, ReadOnlyMemory<byte>>>();
                return builder.Build();
            });
            busConfigurator.Services.AddTransient<IKafkaPublisherExecutor, KafkaPublisherExecutor>();
            busConfigurator.Services.AddTransient<IPublisher, KafkaPublisher>();

            busConfigurator.Services.AddTransient<IMessageParser, MessageParser>();
            busConfigurator.Services.AddTransient<IKafkaMessageHandler, KafkaMessageHandler>();

            busConfigurator.Services.AddSingleton<IConsumerBuilderFactory, ConsumerBuilderFactory>();

            busConfigurator.Services.AddSingleton(typeof(IMessageSubscriber<>), typeof(KafkaMessageSubscriber<>));

            busConfigurator.Services.AddSingleton(typeof(IInfrastructureCreator), typeof(KafkaInfrastructureCreator<>));

            return busConfigurator;
        }
    }
}