using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka.Tests.Integration;

[Trait("Category", "Integration")]
public class KafkaFailureHandlingTests : IClassFixture<KafkaFixture>
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private readonly KafkaFixture _fixture;

    public KafkaFailureHandlingTests(KafkaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_message_should_be_processed_again_after_the_retry_delay_on_a_lock_conflict_while_the_others_keep_flowing()
    {
        var configuration = _fixture.BuildKafkaConfiguration() with { RetryDelay = RetryDelay };
        var conflicting = DummyMessage.CreateEnvelope();
        var other = DummyMessage.CreateEnvelope();

        var attempts = new ConcurrentQueue<(string MessageId, DateTimeOffset At)>();
        var conflictingProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = await BuildServicesAsync(configuration, message =>
        {
            attempts.Enqueue((message.MessageId, DateTimeOffset.UtcNow));

            if (message.MessageId != conflicting.MessageId)
                return;

            if (attempts.Count(a => a.MessageId == conflicting.MessageId) == 1)
                throw new LockException("locked by someone else");

            conflictingProcessed.TrySetResult();
        });
        var publisher = services.GetRequiredService<IPublisher>();
        var sut = services.GetRequiredService<IMessageSubscriber>();

        await sut.StartAsync();
        await publisher.PublishAsync(conflicting);
        await publisher.PublishAsync(other);
        await conflictingProcessed.Task.WaitAsync(Timeout);
        await sut.StopAsync();

        var conflictingAttempts = attempts.Where(a => a.MessageId == conflicting.MessageId).ToArray();
        Assert.Equal(2, conflictingAttempts.Length);
        // the not-before time has millisecond precision
        Assert.True(conflictingAttempts[1].At - conflictingAttempts[0].At >= RetryDelay - TimeSpan.FromMilliseconds(5),
            $"processed again after {conflictingAttempts[1].At - conflictingAttempts[0].At}");

        var otherAttempt = Assert.Single(attempts, a => a.MessageId == other.MessageId);
        Assert.True(otherAttempt.At < conflictingAttempts[1].At, "the other message waited for the retried one");

        Assert.Empty(ReadAll(configuration, DeadLetterTopicOf(configuration)));
    }

    [Fact]
    public async Task A_message_should_go_to_the_dead_letter_topic_without_being_processed_again_on_any_other_failure()
    {
        var configuration = _fixture.BuildKafkaConfiguration();
        var failing = DummyMessage.CreateEnvelope();
        var last = DummyMessage.CreateEnvelope();

        var attempts = new ConcurrentQueue<string>();
        var lastProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = await BuildServicesAsync(configuration, message =>
        {
            attempts.Enqueue(message.MessageId);

            if (message.MessageId == failing.MessageId)
                throw new InvalidOperationException("whoops");

            lastProcessed.TrySetResult();
        });
        var publisher = services.GetRequiredService<IPublisher>();
        var sut = services.GetRequiredService<IMessageSubscriber>();

        await sut.StartAsync();
        await publisher.PublishAsync(failing);
        await publisher.PublishAsync(last);
        await lastProcessed.Task.WaitAsync(Timeout);
        await sut.StopAsync();

        Assert.Single(attempts, id => id == failing.MessageId);

        var deadLetter = Assert.Single(ReadAll(configuration, DeadLetterTopicOf(configuration)));
        Assert.Equal(failing.MessageId, deadLetter.Message.Key);
        Assert.Equal("whoops", Encoding.UTF8.GetString(deadLetter.Message.Headers.GetLastBytes(HeaderNames.Error)));
    }

    private static string DeadLetterTopicOf(KafkaConfiguration configuration)
        => configuration.DefaultQueueReferenceCreator!(typeof(DummyMessage)).DeadLetterTopicName;

    private static async Task<ServiceProvider> BuildServicesAsync(KafkaConfiguration configuration, Action<MessageEnvelope> onMessage)
    {
        var queueReferences = configuration.DefaultQueueReferenceCreator!(typeof(DummyMessage));
        await CreateTopicsAsync(configuration, queueReferences.TopicName, queueReferences.RetryTopicName, queueReferences.DeadLetterTopicName);

        var services = new ServiceCollection();
        services.AddLogging();

        var busConfigurator = Substitute.For<IBusConfigurator>();
        busConfigurator.Services.Returns(services);
        busConfigurator.UseKafkaTransport(configuration);

        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.ClientGroup.Returns($"tests-{Guid.NewGuid():N}");
        systemInfo.ClientId.Returns(Guid.NewGuid().ToString("N"));
        services.AddSingleton(systemInfo);

        var typeResolver = Substitute.For<ITypeResolver>();
        typeResolver.Resolve(typeof(DummyMessage).FullName!).Returns(typeof(DummyMessage));
        services.AddSingleton(typeResolver);

        services.AddSingleton<ISerializer>(new JsonSerializer());

        var descriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        descriptorsResolver.GetRegisteredMessageTypes().Returns(new[] { typeof(DummyMessage) });
        services.AddSingleton(descriptorsResolver);

        var processor = Substitute.For<IMessageProcessor>();
        processor.When(p => p.ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>()))
                 .Do(call => onMessage(call.Arg<MessageEnvelope>()));
        services.AddSingleton(processor);

        return services.BuildServiceProvider();
    }

    // a consumer that subscribes before its topics exist only notices them at the next metadata refresh
    private static async Task CreateTopicsAsync(KafkaConfiguration configuration, params string[] topicNames)
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = configuration.ConnectionString }).Build();
        await adminClient.CreateTopicsAsync(topicNames.Select(name => new TopicSpecification
        {
            Name = name,
            NumPartitions = 1,
            ReplicationFactor = 1
        }));
    }

    private static IReadOnlyList<ConsumeResult<string, byte[]>> ReadAll(KafkaConfiguration configuration, string topicName)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration.ConnectionString,
            GroupId = $"tests-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = true
        };
        using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        consumer.Assign(new TopicPartitionOffset(topicName, 0, Offset.Beginning));

        var messages = new List<ConsumeResult<string, byte[]>>();
        while (true)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(30));
            if (result is null || result.IsPartitionEOF)
                break;
            messages.Add(result);
        }

        consumer.Close();
        return messages;
    }
}
