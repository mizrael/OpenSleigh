using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport.Kafka;

/// <param name="ConnectionString">the bootstrap servers.</param>
/// <param name="DefaultQueueReferenceCreator">optional. Decides the topics of a message type.</param>
[ExcludeFromCodeCoverage]
public record KafkaConfiguration(string ConnectionString, QueueReferencesCreator? DefaultQueueReferenceCreator = null)
{
    /// <summary>
    /// how long the producer holds a message back, waiting for others to send in the same request (linger.ms).
    /// Defaults to zero: the outbox relay publishes one message at a time and waits for each to be acknowledged,
    /// so every millisecond here is added to every message. Raise it only when something else publishes
    /// many messages at once through the same producer.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">the value is negative.</exception>
    public TimeSpan ProducerLinger
    {
        get => _producerLinger;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _producerLinger = value;
        }
    }

    private readonly TimeSpan _producerLinger = TimeSpan.Zero;

    /// <summary>
    /// how long a message waits before it is processed again after a lock conflict, that is when the saga instance
    /// it is for was being processed by another worker. Defaults to 30 seconds.
    /// The message waits in the retry topic of its type (see <see cref="QueueReferences.RetryTopicName"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">the value is negative.</exception>
    public TimeSpan RetryDelay
    {
        get => _retryDelay;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _retryDelay = value;
        }
    }

    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(30);
}

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
            return new ProducerConfig()
            {
                BootstrapServers = kafkaConfig.ConnectionString,
                LingerMs = kafkaConfig.ProducerLinger.TotalMilliseconds
            };
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
        // the handler is a singleton and the saga pipeline is not: it takes a scope factory
        // so that every message is processed by its own pipeline instead of one captured for the life of the process
        busConfigurator.Services.AddSingleton<IKafkaMessageHandler, KafkaMessageHandler>();

        busConfigurator.Services.AddSingleton<IConsumerBuilderFactory, ConsumerBuilderFactory>();

        busConfigurator.Services.AddSingleton<IMessageSubscriber, KafkaMessageSubscriber>();

        // busConfigurator.Services.AddSingleton(typeof(IInfrastructureCreator), typeof(KafkaInfrastructureCreator<>));

        return busConfigurator;
    }
}