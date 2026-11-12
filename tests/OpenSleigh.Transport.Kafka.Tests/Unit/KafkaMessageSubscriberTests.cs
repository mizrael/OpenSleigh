using Confluent.Kafka;
using System;
using System.Collections.Concurrent;
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
