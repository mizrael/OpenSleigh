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
    public async Task StartAsync_should_keep_consuming_after_a_consume_error_that_is_not_fatal()
    {
        var message = new ConsumeResult<string, byte[]> { Topic = "lorem", Partition = 0, Offset = 0 };
        var attempts = 0;
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>())
            .Returns(_ => Interlocked.Increment(ref attempts) switch
            {
                1 => throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]> { Topic = "lorem.retry" },
                    new Error(ErrorCode.TopicAuthorizationFailed, "not authorized")),
                2 => message,
                _ => null
            });

        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = Substitute.For<IKafkaMessageHandler>();
        handler.HandleAsync(message, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                handled.TrySetResult();
                return ValueTask.FromResult(true);
            });

        var sut = CreateSut(consumer, handler, new KafkaSubscriberConfig(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));

        await sut.StartAsync();
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync();
    }

    [Fact]
    public async Task StartAsync_should_store_the_offset_of_every_message_once_it_was_handled()
    {
        var messages = Enumerable.Range(0, 3)
            .Select(offset => new ConsumeResult<string, byte[]> { Topic = "lorem", Partition = 0, Offset = offset })
            .ToArray();
        var pending = new ConcurrentQueue<ConsumeResult<string, byte[]>>(messages);

        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Consume(Arg.Any<int>())
            .Returns(_ => pending.TryDequeue(out var result) ? result : null);
        var lastStored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.When(c => c.StoreOffset(messages[^1])).Do(_ => lastStored.TrySetResult());

        var handler = Substitute.For<IKafkaMessageHandler>();
        handler.HandleAsync(Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>())
               .Returns(ValueTask.FromResult(true));

        var sut = CreateSut(consumer, handler);

        await sut.StartAsync();
        await lastStored.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync();

        Received.InOrder(() =>
        {
            handler.HandleAsync(messages[0], Arg.Any<CancellationToken>());
            consumer.StoreOffset(messages[0]);
            handler.HandleAsync(messages[1], Arg.Any<CancellationToken>());
            consumer.StoreOffset(messages[1]);
            handler.HandleAsync(messages[2], Arg.Any<CancellationToken>());
            consumer.StoreOffset(messages[2]);
        });
    }

    [Fact]
    public async Task StartAsync_should_keep_consuming_when_the_consumer_stores_offsets_by_itself()
    {
        const int messagesCount = 3;
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
