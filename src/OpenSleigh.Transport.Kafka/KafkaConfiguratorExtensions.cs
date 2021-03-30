using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    [ExcludeFromCodeCoverage]
    public record KafkaConfiguration(string ConnectionString, Func<Type, QueueReferences> DefaultQueueReferenceCreator = null);

    [ExcludeFromCodeCoverage]
    public static class KafkaConfiguratorExtensions
    {
        public static IBusConfigurator UseKafkaTransport(this IBusConfigurator busConfigurator,
            KafkaConfiguration config) => UseKafkaTransport(busConfigurator, config, null);
        
        public static IBusConfigurator UseKafkaTransport(this IBusConfigurator busConfigurator,
            KafkaConfiguration config,
            Action<IKafkaBusConfigurationBuilder> builderFunc)
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
                var builder = new ProducerBuilder<Guid, byte[]>(config);
                builder.SetKeySerializer(new KeySerializer<Guid>());

                return builder;
            });
            busConfigurator.Services.AddSingleton<IProducer<Guid, byte[]>>(ctx =>
            {
                var builder = ctx.GetRequiredService<ProducerBuilder<Guid, byte[]>>();
                return builder.Build();
            });
            busConfigurator.Services.AddTransient<IKafkaPublisherExecutor, KafkaPublisherExecutor>();
            busConfigurator.Services.AddTransient<IPublisher, KafkaPublisher>();

            busConfigurator.Services.AddTransient<IMessageParser, MessageParser>();
            busConfigurator.Services.AddSingleton<IKafkaMessageHandler, KafkaMessageHandler>();

            busConfigurator.Services.AddSingleton<IGroupIdFactory, DefaultGroupIdFactory>();
            busConfigurator.Services.AddSingleton<IConsumerBuilderFactory, ConsumerBuilderFactory>();

            builderFunc?.Invoke(new DefaultKafkaBusConfigurationBuilder(busConfigurator));
            
            return busConfigurator;
        }
    }
}