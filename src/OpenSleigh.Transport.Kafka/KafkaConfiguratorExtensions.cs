using System;
using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    [ExcludeFromCodeCoverage]
    public record KafkaConfiguration(string ConnectionString, string ConsumerGroup, Func<Type, QueueReferences> DefaultQueueReferenceCreator = null);

    [ExcludeFromCodeCoverage]
    public static class KafkaConfiguratorExtensions
    {
        public static IBusConfigurator UseKafkaTransport(this IBusConfigurator busConfigurator,
            KafkaConfiguration config) => UseKafkaTransport(busConfigurator, config, null);
        
        public static IBusConfigurator UseKafkaTransport(this IBusConfigurator busConfigurator,
            KafkaConfiguration config,
            Action<IKafkaBusConfigurationBuilder> builderFunc)
        {
            busConfigurator.Services.AddSingleton<IQueueReferenceFactory>(ctx =>
            {
                return new QueueReferenceFactory(ctx, config.DefaultQueueReferenceCreator);
            });

            busConfigurator.Services.AddSingleton(new ProducerConfig() { BootstrapServers = config.ConnectionString } );
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

            busConfigurator.Services.AddSingleton(new ConsumerConfig()
            {
                GroupId = config.ConsumerGroup,
                BootstrapServers = config.ConnectionString,
                AutoOffsetReset = AutoOffsetReset.Earliest,               
                EnablePartitionEof = true
            });
            busConfigurator.Services.AddSingleton(ctx =>
            {
                var config = ctx.GetRequiredService<ConsumerConfig>();
                var builder = new ConsumerBuilder<Guid, byte[]>(config);
                builder.SetKeyDeserializer(new GuidDeserializer());

                return builder;
            });

            builderFunc?.Invoke(new DefaultKafkaBusConfigurationBuilder(busConfigurator));
            
            return busConfigurator;
        }
    }
}