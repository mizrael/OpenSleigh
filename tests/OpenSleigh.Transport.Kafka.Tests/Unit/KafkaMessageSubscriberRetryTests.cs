using Confluent.Kafka;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

/// <summary>
/// what the subscriber does with the messages of a retry topic, and with the ones it could not deal with.
/// </summary>
public class KafkaMessageSubscriberRetryTests
{
    private const string Group = "mine";
    private const string Topic = "lorem";
    private const string RetryTopic = "lorem.retry";
    private static readonly TopicPartition RetryPartition = new(RetryTopic, 0);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_retried_message_should_be_held_until_it_is_due_without_keeping_the_other_partitions_waiting()
    {
        var harness = new Harness();
        var notBefore = InMilliseconds(DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(600));
        var retried = Retried(7, notBefore);
        var other = Message(Topic, 3);
        harness.Deliver(retried, other);

        await harness.RunUntilStoredAsync(retried, retryDelay: TimeSpan.FromSeconds(30));

        Assert.True(harness.HandledAt(retried) >= notBefore, $"handled {notBefore - harness.HandledAt(retried)} early");
        Assert.True(harness.HandledAt(other) < notBefore, "the other partition waited for the retried message");
        Received.InOrder(() =>
        {
            harness.Consumer.Pause(Arg.Is<IEnumerable<TopicPartition>>(partitions => partitions.Single() == RetryPartition));
            harness.Consumer.StoreOffset(other);
            harness.Consumer.Resume(Arg.Is<IEnumerable<TopicPartition>>(partitions => partitions.Single() == RetryPartition));
            harness.Consumer.StoreOffset(retried);
        });
    }

    [Fact]
    public async Task A_not_before_time_should_be_ignored_on_a_topic_that_is_not_a_retry_topic()
    {
        var harness = new Harness();
        var message = Message(Topic, 3, RetrySchedule.CreateHeader(DateTimeOffset.UtcNow + TimeSpan.FromHours(1)));
        harness.Deliver(message);

        await harness.RunUntilStoredAsync(message, retryDelay: TimeSpan.FromHours(1));

        harness.Consumer.DidNotReceiveWithAnyArgs().Pause(default!);
    }

    [Fact]
    public async Task A_retried_message_should_not_wait_longer_than_the_retry_delay_whatever_its_not_before_time()
    {
        var harness = new Harness();
        var retryDelay = TimeSpan.FromMilliseconds(300);
        var retried = Retried(7, DateTimeOffset.UtcNow + TimeSpan.FromDays(365));
        harness.Deliver(retried);

        var startedAt = DateTimeOffset.UtcNow;
        await harness.RunUntilStoredAsync(retried, retryDelay);

        var waited = harness.HandledAt(retried) - startedAt;
        Assert.InRange(waited, retryDelay - TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_message_retried_by_another_group_should_be_skipped_without_waiting_for_it()
    {
        var harness = new Harness();
        var theirs = Retried(7, DateTimeOffset.UtcNow + TimeSpan.FromHours(1), group: "theirs");
        var mine = Retried(8, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1));
        harness.Deliver(theirs, mine);

        await harness.RunUntilStoredAsync(mine, retryDelay: TimeSpan.FromHours(1));

        await harness.Handler.DidNotReceive().HandleAsync(theirs, Arg.Any<CancellationToken>());
        await harness.Handler.Received(1).HandleAsync(mine, Arg.Any<CancellationToken>());
        harness.Consumer.DidNotReceiveWithAnyArgs().Pause(default!);
    }

    [Fact]
    public async Task The_offset_of_a_message_retried_by_another_group_should_be_stored_when_nothing_follows_it()
    {
        var harness = new Harness();
        var theirs = Retried(7, DateTimeOffset.UtcNow + TimeSpan.FromHours(1), group: "theirs");
        harness.Deliver(theirs);

        await harness.RunUntilStoredAsync(theirs, retryDelay: TimeSpan.FromHours(1));

        await harness.Handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
    }

    [Fact]
    public async Task The_offsets_of_handled_messages_should_be_stored_before_their_partition_is_given_up()
    {
        var harness = new Harness();
        var handled = Message(Topic, 3);
        var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storedWhenRevoked = false;
        var deliveries = 0;
        // a consumer with a message always ready is never idle: the only chance to store is the rebalance
        harness.Consumer.Consume(Arg.Any<int>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref deliveries) == 1)
                return handled;

            if (!revoked.Task.IsCompleted)
            {
                harness.Builder.Revoke(harness.Consumer, handled.TopicPartition);
                storedWhenRevoked = harness.Events.Any(e => e.Name == "store:3");
                revoked.TrySetResult();
            }

            Thread.Sleep(5);
            return null;
        });

        var sut = harness.CreateSut(TimeSpan.FromSeconds(30));
        await sut.StartAsync();
        await revoked.Task.WaitAsync(Timeout);
        await sut.StopAsync();

        Assert.True(storedWhenRevoked);
    }

    [Fact]
    public async Task A_message_that_was_not_dealt_with_should_keep_its_partition_waiting_until_it_is()
    {
        var harness = new Harness();
        var retryDelay = TimeSpan.FromMilliseconds(300);
        var failing = Message(Topic, 3);
        var attempts = 0;
        harness.IsHandled = _ => Interlocked.Increment(ref attempts) > 1;
        harness.Deliver(failing);

        await harness.RunUntilStoredAsync(failing, retryDelay);

        Assert.Equal(2, attempts);
        var handled = harness.Events.Where(e => e.Name == "handle:3").ToArray();
        Assert.True(handled[1].At - handled[0].At >= retryDelay - TimeSpan.FromMilliseconds(50));
        Assert.Single(harness.Events, e => e.Name == "store:3");
        var partition = new TopicPartition(Topic, 0);
        Received.InOrder(() =>
        {
            harness.Consumer.Pause(Arg.Is<IEnumerable<TopicPartition>>(partitions => partitions.Single() == partition));
            harness.Consumer.Resume(Arg.Is<IEnumerable<TopicPartition>>(partitions => partitions.Single() == partition));
            harness.Consumer.StoreOffset(failing);
        });
    }

    [Fact]
    public async Task Held_messages_that_are_due_should_be_processed_one_per_poll()
    {
        var harness = new Harness();
        var notBefore = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(300);
        var first = Retried(7, notBefore);
        var second = Retried(8, notBefore);
        harness.Deliver(first, second);

        await harness.RunUntilStoredAsync(second, retryDelay: TimeSpan.FromSeconds(30));

        var names = harness.Events.Select(e => e.Name).ToList();
        var between = names.Skip(names.IndexOf("handle:7")).TakeWhile(name => name != "handle:8");
        Assert.Contains("consume", between);
    }

    [Fact]
    public async Task Held_messages_should_be_left_to_the_next_consumer_when_their_partition_is_taken_away()
    {
        var harness = new Harness();
        var retried = Retried(7, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(300));
        harness.Deliver(retried);
        var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnIdle = () =>
        {
            if (revoked.Task.IsCompleted)
                return;
            harness.Builder.Revoke(harness.Consumer, RetryPartition);
            revoked.TrySetResult();
        };

        var sut = harness.CreateSut(TimeSpan.FromSeconds(30));
        await sut.StartAsync();
        await revoked.Task.WaitAsync(Timeout);
        await Task.Delay(600);
        await sut.StopAsync();

        await harness.Handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
        harness.Consumer.DidNotReceiveWithAnyArgs().StoreOffset(default(ConsumeResult<string, byte[]>)!);
        harness.Consumer.Received(1).Resume(Arg.Is<IEnumerable<TopicPartition>>(partitions => partitions.Single() == RetryPartition));
    }

    [Fact]
    public async Task A_held_message_should_still_be_processed_when_its_partition_cannot_be_paused_or_resumed()
    {
        var harness = new Harness();
        harness.Consumer.When(c => c.Pause(Arg.Any<IEnumerable<TopicPartition>>()))
                        .Do(_ => throw new KafkaException(new Error(ErrorCode.Local_UnknownPartition, "not assigned")));
        harness.Consumer.When(c => c.Resume(Arg.Any<IEnumerable<TopicPartition>>()))
                        .Do(_ => throw new KafkaException(new Error(ErrorCode.Local_UnknownPartition, "not assigned")));
        var retried = Retried(7, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(200));
        var after = Message(Topic, 4);
        harness.Deliver(retried);

        var sut = harness.CreateSut(TimeSpan.FromSeconds(30));
        await sut.StartAsync();
        await harness.WaitUntilStoredAsync(retried);
        harness.Deliver(after);
        await harness.WaitUntilStoredAsync(after);
        await sut.StopAsync();
    }

    [Fact]
    public async Task The_loop_should_go_on_when_an_offset_cannot_be_stored()
    {
        var harness = new Harness();
        var first = Message(Topic, 3);
        var second = Message("ipsum", 4);
        harness.Consumer.When(c => c.StoreOffset(first))
                        .Do(_ => throw new KafkaException(new Error(ErrorCode.Local_State, "not assigned")));
        harness.Deliver(first, second);

        await harness.RunUntilStoredAsync(second, retryDelay: TimeSpan.FromSeconds(30));

        await harness.Handler.Received(1).HandleAsync(first, Arg.Any<CancellationToken>());
    }

    private static DateTimeOffset InMilliseconds(DateTimeOffset time)
        => DateTimeOffset.FromUnixTimeMilliseconds(time.ToUnixTimeMilliseconds());

    private static ConsumeResult<string, byte[]> Retried(long offset, DateTimeOffset notBefore, string group = Group)
        => Message(RetryTopic, offset, RetrySchedule.CreateHeader(notBefore), RetryTopics.CreateGroupHeader(group));

    private static ConsumeResult<string, byte[]> Message(string topic, long offset, params Header[] headers)
    {
        var messageHeaders = new Headers();
        foreach (var header in headers)
            messageHeaders.Add(header);

        return new ConsumeResult<string, byte[]>
        {
            Topic = topic,
            Partition = 0,
            Offset = offset,
            Message = new Message<string, byte[]> { Headers = messageHeaders }
        };
    }

    private sealed record Event(string Name, DateTimeOffset At);

    private sealed class Harness
    {
        private readonly ConcurrentQueue<ConsumeResult<string, byte[]>> _pending = new();
        private readonly ConcurrentQueue<Event> _events = new();
        private readonly ConcurrentDictionary<ConsumeResult<string, byte[]>, TaskCompletionSource> _stored = new();

        public Harness()
        {
            Consumer = Substitute.For<IConsumer<string, byte[]>>();
            Builder = new StubConsumerBuilder(Consumer);
            Consumer.Consume(Arg.Any<int>()).Returns(_ =>
            {
                Record("consume");
                if (_pending.TryDequeue(out var result))
                    return result;

                OnIdle?.Invoke();
                Thread.Sleep(5);
                return null;
            });
            Consumer.When(c => c.StoreOffset(Arg.Any<ConsumeResult<string, byte[]>>()))
                    .Do(call =>
                    {
                        var stored = call.Arg<ConsumeResult<string, byte[]>>();
                        Record($"store:{stored.Offset.Value}");
                        StoredSignal(stored).TrySetResult();
                    });

            Handler = Substitute.For<IKafkaMessageHandler>();
            Handler.HandleAsync(Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>())
                   .Returns(call =>
                   {
                       var message = call.Arg<ConsumeResult<string, byte[]>>();
                       Record($"handle:{message.Offset.Value}");
                       return ValueTask.FromResult(IsHandled(message));
                   });
        }

        public IConsumer<string, byte[]> Consumer { get; }
        public IKafkaMessageHandler Handler { get; }
        public StubConsumerBuilder Builder { get; }
        public Func<ConsumeResult<string, byte[]>, bool> IsHandled { get; set; } = _ => true;

        /// <summary>called by the consume loop, from inside Consume, when there is nothing to deliver.</summary>
        public Action? OnIdle { get; set; }

        public IReadOnlyCollection<Event> Events => _events.ToArray();

        public void Deliver(params ConsumeResult<string, byte[]>[] messages)
        {
            foreach (var message in messages)
                _pending.Enqueue(message);
        }

        public DateTimeOffset HandledAt(ConsumeResult<string, byte[]> message)
            => _events.Last(e => e.Name == $"handle:{message.Offset.Value}").At;

        public Task WaitUntilStoredAsync(ConsumeResult<string, byte[]> message)
            => StoredSignal(message).Task.WaitAsync(Timeout);

        public async Task RunUntilStoredAsync(ConsumeResult<string, byte[]> message, TimeSpan retryDelay)
        {
            var sut = CreateSut(retryDelay);
            await sut.StartAsync();
            await WaitUntilStoredAsync(message);
            await sut.StopAsync();
        }

        public KafkaMessageSubscriber CreateSut(TimeSpan retryDelay)
        {
            var builderFactory = Substitute.For<IConsumerBuilderFactory>();
            builderFactory.Create<string, byte[]>().Returns(Builder);

            var descriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
            descriptorsResolver.GetRegisteredMessageTypes().Returns(new[] { typeof(DummyMessage) });
            var queueReferenceFactory = Substitute.For<IQueueReferenceFactory>();
            queueReferenceFactory.Create(typeof(DummyMessage)).Returns(new QueueReferences(Topic, "lorem.dead"));

            var systemInfo = Substitute.For<ISystemInfo>();
            systemInfo.ClientGroup.Returns(Group);

            return new KafkaMessageSubscriber(
                builderFactory,
                descriptorsResolver,
                queueReferenceFactory,
                Handler,
                new KafkaSubscriberConfig(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)),
                systemInfo,
                new KafkaConfiguration("lorem") { RetryDelay = retryDelay });
        }

        private void Record(string name) => _events.Enqueue(new Event(name, DateTimeOffset.UtcNow));

        private TaskCompletionSource StoredSignal(ConsumeResult<string, byte[]> message)
            => _stored.GetOrAdd(message, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
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

        public void Revoke(IConsumer<string, byte[]> consumer, params TopicPartition[] partitions)
            => PartitionsRevokedHandler(consumer, partitions.Select(partition => new TopicPartitionOffset(partition, Offset.Unset)).ToList());
    }
}
