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
        var conflictingId = ConsumedId(conflicting);
        var conflictingProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = await BuildServicesAsync(configuration, message =>
        {
            attempts.Enqueue((message.MessageId, DateTimeOffset.UtcNow));

            if (message.MessageId != conflictingId)
                return;

            if (attempts.Count(a => a.MessageId == conflictingId) == 1)
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

        var conflictingAttempts = attempts.Where(a => a.MessageId == conflictingId).ToArray();
        Assert.Equal(2, conflictingAttempts.Length);
        // the not-before time has millisecond precision
        Assert.True(conflictingAttempts[1].At - conflictingAttempts[0].At >= RetryDelay - TimeSpan.FromMilliseconds(5),
            $"processed again after {conflictingAttempts[1].At - conflictingAttempts[0].At}");

        var otherAttempt = Assert.Single(attempts, a => a.MessageId == ConsumedId(other));
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
        var failingId = ConsumedId(failing);
        var lastProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = await BuildServicesAsync(configuration, message =>
        {
            attempts.Enqueue(message.MessageId);

            if (message.MessageId == failingId)
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

        Assert.Single(attempts, id => id == failingId);

        // the dead letter has the id the message was processed with, not one quoted once more
        var deadLetter = Assert.Single(ReadAll(configuration, DeadLetterTopicOf(configuration)));
        Assert.Equal(failingId, deadLetter.Message.Key);
        Assert.Equal("whoops", Encoding.UTF8.GetString(deadLetter.Message.Headers.GetLastBytes(HeaderNames.Error)));
    }

    [Fact]
    public async Task A_message_retried_by_one_group_should_not_be_processed_again_by_the_others()
    {
        var configuration = _fixture.BuildKafkaConfiguration() with { RetryDelay = RetryDelay };
        var message = DummyMessage.CreateEnvelope();

        var conflictingGroupAttempts = 0;
        var otherGroupAttempts = 0;
        var processedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var conflictingGroup = await BuildServicesAsync(configuration, _ =>
        {
            if (Interlocked.Increment(ref conflictingGroupAttempts) == 1)
                throw new LockException("locked by someone else");

            processedAgain.TrySetResult();
        });
        await using var otherGroup = await BuildServicesAsync(configuration, _ => Interlocked.Increment(ref otherGroupAttempts));
        var conflictingSubscriber = conflictingGroup.GetRequiredService<IMessageSubscriber>();
        var otherSubscriber = otherGroup.GetRequiredService<IMessageSubscriber>();

        await conflictingSubscriber.StartAsync();
        await otherSubscriber.StartAsync();
        await conflictingGroup.GetRequiredService<IPublisher>().PublishAsync(message);
        await processedAgain.Task.WaitAsync(Timeout);
        // time for the other group to read the retry topic too
        await Task.Delay(TimeSpan.FromSeconds(5));
        await conflictingSubscriber.StopAsync();
        await otherSubscriber.StopAsync();

        Assert.Equal(2, conflictingGroupAttempts);
        Assert.Equal(1, otherGroupAttempts);
    }

    [Fact]
    public void A_consumer_that_stores_offsets_by_itself_should_refuse_to_be_told_one_with_the_error_the_subscriber_looks_for()
    {
        var configuration = _fixture.BuildKafkaConfiguration();
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration.ConnectionString,
            GroupId = $"tests-{Guid.NewGuid():N}",
            EnableAutoOffsetStore = true
        };
        using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        var topicName = configuration.DefaultQueueReferenceCreator!(typeof(DummyMessage)).TopicName;
        consumer.Assign(new TopicPartitionOffset(topicName, 0, Offset.Beginning));

        var failure = Assert.ThrowsAny<KafkaException>(() => consumer.StoreOffset(new TopicPartitionOffset(topicName, 0, 1)));

        Assert.Equal(ErrorCode.Local_InvalidArg, failure.Error.Code);
    }

    // what a consumer reads as the id of a message: its key is written as JSON
    private static string ConsumedId(MessageEnvelope message)
        => System.Text.Json.JsonSerializer.Serialize(message.MessageId);

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
        try
        {
            await adminClient.CreateTopicsAsync(topicNames.Select(name => new TopicSpecification
            {
                Name = name,
                NumPartitions = 1,
                ReplicationFactor = 1
            }));
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError))
        {
            // created by another service of the same test
        }
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
