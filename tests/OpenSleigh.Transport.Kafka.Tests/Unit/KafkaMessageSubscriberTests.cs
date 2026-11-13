using Confluent.Kafka;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class KafkaMessageSubscriberTests
{
    [Fact]
    public async Task StartAsync_should_consume_ready_messages_without_pausing_between_them()
    {
        const int messagesCount = 20;
        var pending = new ConcurrentQueue<ConsumeResult<string, byte[]>>(
            Enumerable.Range(0, messagesCount).Select(_ => new ConsumeResult<string, byte[]>()));

        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>())
            .Returns(_ => pending.TryDequeue(out var result) ? result : null);

        var handled = 0;
        var allHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = Substitute.For<IKafkaMessageHandler>();
        handler.HandleAsync(Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref handled) == messagesCount)
                    allHandled.TrySetResult();
                return ValueTask.FromResult(true);
            });

        var sut = CreateSut(consumer, handler);

        var stopwatch = Stopwatch.StartNew();
        await sut.StartAsync();
        await allHandled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        stopwatch.Stop();
        await sut.StopAsync();

        // with a 250 ms pause after every message, 20 messages would take 5 seconds
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2.5), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task StartAsync_should_pause_between_consume_attempts_when_a_delay_is_configured()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>()).Returns((ConsumeResult<string, byte[]>?)null);
        var config = new KafkaSubscriberConfig(TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(1));

        var sut = CreateSut(consumer, Substitute.For<IKafkaMessageHandler>(), config);

        await sut.StartAsync();
        await Task.Delay(200);
        await sut.StopAsync();

        consumer.Received(1).Consume(Arg.Any<int>());
    }

    [Fact]
    public async Task StartAsync_should_wait_before_consuming_again_when_the_topic_is_unknown()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>())
            .Returns(_ => throw new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.UnknownTopicOrPart, "unknown topic or partition")));
        var config = new KafkaSubscriberConfig(TimeSpan.Zero, TimeSpan.FromMilliseconds(200));

        var sut = CreateSut(consumer, Substitute.For<IKafkaMessageHandler>(), config);

        await sut.StartAsync();
        await Task.Delay(500);
        await sut.StopAsync();

        // without the wait this would be many thousands of calls
        var calls = consumer.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IConsumer<string, byte[]>.Consume));
        Assert.InRange(calls, 1, 6);
    }

    [Fact]
    public async Task Dispose_should_not_close_the_consumer_again_after_StopAsync()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>()).Returns((ConsumeResult<string, byte[]>?)null);

        var sut = CreateSut(consumer, Substitute.For<IKafkaMessageHandler>());

        await sut.StartAsync();
        await sut.StopAsync();
        sut.Dispose();

        consumer.Received(1).Close();
    }

    [Fact]
    public async Task Dispose_alone_should_close_the_consumer_once_after_the_consume_loop_has_ended()
    {
        var consuming = 0;
        var closedWhileConsuming = false;
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>()).Returns(_ =>
        {
            Interlocked.Exchange(ref consuming, 1);
            Thread.Sleep(50);
            Interlocked.Exchange(ref consuming, 0);
            return (ConsumeResult<string, byte[]>?)null;
        });
        consumer.When(c => c.Close()).Do(_ => closedWhileConsuming |= Volatile.Read(ref consuming) == 1);

        var sut = CreateSut(consumer, Substitute.For<IKafkaMessageHandler>());

        await sut.StartAsync();
        await Task.Delay(100);
        sut.Dispose();

        consumer.Received(1).Close();
        Assert.False(closedWhileConsuming);
    }

    [Fact]
    public async Task Dispose_and_StopAsync_should_be_harmless_after_Dispose()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>()).Returns((ConsumeResult<string, byte[]>?)null);

        var sut = CreateSut(consumer, Substitute.For<IKafkaMessageHandler>());

        await sut.StartAsync();
        sut.Dispose();
        sut.Dispose();
        await sut.StopAsync();

        consumer.Received(1).Close();
        consumer.Received(1).Dispose();
    }

    [Fact]
    public async Task StartAsync_should_subscribe_to_the_topic_and_to_the_retry_topic_of_every_message_type()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>()).Returns((ConsumeResult<string, byte[]>?)null);

        var builderFactory = Substitute.For<IConsumerBuilderFactory>();
        builderFactory.Create<string, byte[]>().Returns(new StubConsumerBuilder(consumer));
        var descriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        descriptorsResolver.GetRegisteredMessageTypes().Returns(new[] { typeof(DummyMessage) });
        var queueReferenceFactory = Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.Create(typeof(DummyMessage)).Returns(new QueueReferences("lorem", "lorem.dead"));

        var sut = new KafkaMessageSubscriber(builderFactory, descriptorsResolver, queueReferenceFactory, Substitute.For<IKafkaMessageHandler>());

        await sut.StartAsync();
        await Task.Delay(100);
        await sut.StopAsync();

        consumer.Received(1).Subscribe(Arg.Is<IEnumerable<string>>(topics => topics.SequenceEqual(new[] { "lorem", "lorem.retry" })));
    }

    [Fact]
    public async Task StartAsync_should_hold_a_retried_message_until_it_is_due_without_keeping_the_other_partitions_waiting()
    {
        var retryDelay = TimeSpan.FromMilliseconds(600);
        // what the header carries: a time in milliseconds
        var notBefore = DateTimeOffset.FromUnixTimeMilliseconds((DateTimeOffset.UtcNow + retryDelay).ToUnixTimeMilliseconds());
        var retried = new ConsumeResult<string, byte[]>
        {
            Topic = "lorem.retry",
            Partition = 0,
            Offset = 7,
            Message = new Message<string, byte[]> { Headers = new Headers { RetrySchedule.CreateHeader(notBefore) } }
        };
        var other = new ConsumeResult<string, byte[]> { Topic = "lorem", Partition = 0, Offset = 3, Message = new Message<string, byte[]>() };
        var pending = new ConcurrentQueue<ConsumeResult<string, byte[]>>(new[] { retried, other });

        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>())
            .Returns(_ => pending.TryDequeue(out var result) ? result : null);

        var handledAt = new ConcurrentDictionary<ConsumeResult<string, byte[]>, DateTimeOffset>();
        var retriedStored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storedBeforeHandling = false;
        consumer.When(c => c.StoreOffset(Arg.Any<ConsumeResult<string, byte[]>>()))
                .Do(call =>
                {
                    var stored = call.Arg<ConsumeResult<string, byte[]>>();
                    storedBeforeHandling |= !handledAt.ContainsKey(stored);
                    if (stored == retried)
                        retriedStored.TrySetResult();
                });
        var handler = Substitute.For<IKafkaMessageHandler>();
        handler.HandleAsync(Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                handledAt[call.Arg<ConsumeResult<string, byte[]>>()] = DateTimeOffset.UtcNow;
                return ValueTask.FromResult(true);
            });

        var sut = CreateSut(consumer, handler, new KafkaSubscriberConfig(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));

        await sut.StartAsync();
        await retriedStored.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync();

        Assert.True(handledAt[retried] >= notBefore, $"handled {notBefore - handledAt[retried]} early");
        Assert.True(handledAt[other] < notBefore, "the other partition waited for the retried message");
        Assert.False(storedBeforeHandling);

        var retryPartition = new TopicPartition("lorem.retry", 0);
        Received.InOrder(() =>
        {
            consumer.Pause(Arg.Is<IEnumerable<TopicPartition>>(partitions => partitions.Single() == retryPartition));
            consumer.StoreOffset(other);
            consumer.Resume(Arg.Is<IEnumerable<TopicPartition>>(partitions => partitions.Single() == retryPartition));
            consumer.StoreOffset(retried);
        });
    }

    [Fact]
    public async Task StartAsync_should_store_the_offset_of_the_last_message_of_a_partition_instead_of_one_per_message()
    {
        const int messagesCount = 250;
        var pending = new ConcurrentQueue<ConsumeResult<string, byte[]>>(
            Enumerable.Range(0, messagesCount).Select(offset => new ConsumeResult<string, byte[]> { Topic = "lorem", Partition = 0, Offset = offset }));
        var last = pending.Last();

        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>())
            .Returns(_ => pending.TryDequeue(out var result) ? result : null);
        var lastStored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.When(c => c.StoreOffset(last)).Do(_ => lastStored.TrySetResult());

        var handler = Substitute.For<IKafkaMessageHandler>();
        handler.HandleAsync(Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>())
               .Returns(ValueTask.FromResult(true));

        var sut = CreateSut(consumer, handler);

        await sut.StartAsync();
        await lastStored.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync();

        // once every 100 messages, and once more when there is nothing left to consume
        var storeCalls = consumer.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IConsumer<string, byte[]>.StoreOffset));
        Assert.Equal(3, storeCalls);
    }

    [Fact]
    public async Task StartAsync_should_end_up_storing_the_offset_of_the_last_message_of_every_partition_when_they_alternate()
    {
        ConsumeResult<string, byte[]> Message(string topic, long offset) => new() { Topic = topic, Partition = 0, Offset = offset };
        var messages = new[] { Message("lorem", 0), Message("ipsum", 0), Message("lorem", 1), Message("ipsum", 1), Message("lorem", 2) };
        var pending = new ConcurrentQueue<ConsumeResult<string, byte[]>>(messages);

        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>())
            .Returns(_ => pending.TryDequeue(out var result) ? result : null);
        var lastStoredByTopic = new ConcurrentDictionary<string, long>();
        var allStored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.When(c => c.StoreOffset(Arg.Any<ConsumeResult<string, byte[]>>()))
                .Do(call =>
                {
                    var stored = call.Arg<ConsumeResult<string, byte[]>>();
                    lastStoredByTopic[stored.Topic] = stored.Offset.Value;
                    if (stored == messages[^1])
                        allStored.TrySetResult();
                });

        var handler = Substitute.For<IKafkaMessageHandler>();
        handler.HandleAsync(Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>())
               .Returns(ValueTask.FromResult(true));

        var sut = CreateSut(consumer, handler);

        await sut.StartAsync();
        await allStored.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync();

        Assert.Equal(2, lastStoredByTopic["lorem"]);
        Assert.Equal(1, lastStoredByTopic["ipsum"]);
    }

    [Fact]
    public async Task StartAsync_should_keep_consuming_when_the_consumer_stores_offsets_by_itself()
    {
        const int messagesCount = 250;
        var pending = new ConcurrentQueue<ConsumeResult<string, byte[]>>(
            Enumerable.Range(0, messagesCount).Select(_ => new ConsumeResult<string, byte[]>()));

        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>())
            .Returns(_ => pending.TryDequeue(out var result) ? result : null);
        consumer.When(c => c.StoreOffset(Arg.Any<ConsumeResult<string, byte[]>>()))
                .Do(_ => throw new KafkaException(new Error(ErrorCode.Local_InvalidArg, "enable.auto.offset.store is on")));

        var handled = 0;
        var allHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = Substitute.For<IKafkaMessageHandler>();
        handler.HandleAsync(Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref handled) == messagesCount)
                    allHandled.TrySetResult();
                return ValueTask.FromResult(true);
            });

        var sut = CreateSut(consumer, handler);

        await sut.StartAsync();
        await allHandled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync();

        consumer.Received(1).StoreOffset(Arg.Any<ConsumeResult<string, byte[]>>());
    }

    private static KafkaMessageSubscriber CreateSut(
        IConsumer<string, byte[]> consumer,
        IKafkaMessageHandler handler,
        KafkaSubscriberConfig? config = null)
    {
        var builderFactory = Substitute.For<IConsumerBuilderFactory>();
        builderFactory.Create<string, byte[]>().Returns(new StubConsumerBuilder(consumer));

        var descriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        descriptorsResolver.GetRegisteredMessageTypes().Returns(Array.Empty<Type>());

        return new KafkaMessageSubscriber(
            builderFactory,
            descriptorsResolver,
            Substitute.For<IQueueReferenceFactory>(),
            handler,
            config);
    }

    private sealed class StubConsumerBuilder : ConsumerBuilder<string, byte[]>
    {
        private readonly IConsumer<string, byte[]> _consumer;

        public StubConsumerBuilder(IConsumer<string, byte[]> consumer)
            : base(new ConsumerConfig { GroupId = "tests" })
        {
            _consumer = consumer;
        }

        public override IConsumer<string, byte[]> Build() => _consumer;
    }
}
