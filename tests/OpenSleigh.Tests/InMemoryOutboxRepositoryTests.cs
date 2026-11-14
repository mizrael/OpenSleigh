using OpenSleigh.InMemory.Outbox;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests;

public class InMemoryOutboxRepositoryTests
{
    [Fact]
    public async Task AppendAsync_should_add_messages()
    {
        var sut = new InMemoryOutboxRepository();
        var envelope = DummyMessage.CreateEnvelope();

        var result = await sut.AppendAsync(new[] { envelope });

        Assert.Equal(OutboxAppendResult.Success, result);
    }

    [Fact]
    public async Task AppendAsync_should_return_duplicate_for_same_message_id()
    {
        var sut = new InMemoryOutboxRepository();
        var envelope = DummyMessage.CreateEnvelope();

        await sut.AppendAsync(new[] { envelope });
        var result = await sut.AppendAsync(new[] { envelope });

        Assert.Equal(OutboxAppendResult.Duplicate, result);
    }

    [Fact]
    public async Task AppendAsync_should_throw_when_messages_is_null()
    {
        var sut = new InMemoryOutboxRepository();
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.AppendAsync(null!).AsTask());
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_all_appended_messages()
    {
        var sut = new InMemoryOutboxRepository();
        var envelope1 = DummyMessage.CreateEnvelope();
        var envelope2 = DummyMessage.CreateEnvelope();

        await sut.AppendAsync(new[] { envelope1, envelope2 });
        var pending = await sut.ReadPendingAsync();

        Assert.Equal(2, pending.Count());
    }

    [Fact]
    public async Task DeleteAsync_should_remove_message()
    {
        var sut = new InMemoryOutboxRepository();
        var envelope = DummyMessage.CreateEnvelope();

        await sut.AppendAsync(new[] { envelope });
        await sut.DeleteAsync(envelope);

        var pending = await sut.ReadPendingAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task DeleteAsync_should_throw_when_message_is_null()
    {
        var sut = new InMemoryOutboxRepository();
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.DeleteAsync(null!).AsTask());
    }

    [Fact]
    public async Task ReadPendingAsync_should_not_return_a_message_before_it_is_due()
    {
        var sut = new InMemoryOutboxRepository();
        var immediate = DummyMessage.CreateEnvelope();
        var delayed = DummyMessage.CreateEnvelope(delay: TimeSpan.FromHours(1));

        await sut.AppendAsync(new[] { delayed, immediate });
        var pending = await sut.ReadPendingAsync();

        Assert.Equal(immediate.MessageId, Assert.Single(pending).MessageId);
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_a_delayed_message_once_it_is_due_until_it_is_deleted()
    {
        var clock = new SettableClock();
        var sut = new InMemoryOutboxRepository(clock);
        var delayed = CreateEnvelope(deliverAt: clock.UtcNow + ShortDelay);
        var stillWaiting = CreateEnvelope(deliverAt: clock.UtcNow + LongDelay);

        await sut.AppendAsync(new[] { stillWaiting, delayed });
        Assert.Empty(await sut.ReadPendingAsync());

        clock.UtcNow += ShortDelay;

        Assert.Equal(delayed.MessageId, Assert.Single(await sut.ReadPendingAsync()).MessageId);
        Assert.Equal(delayed.MessageId, Assert.Single(await sut.ReadPendingAsync()).MessageId);

        await sut.DeleteAsync(delayed);
        Assert.Empty(await sut.ReadPendingAsync());
    }

    [Fact]
    public async Task DeleteAsync_should_remove_a_delayed_message_that_is_not_due_yet()
    {
        var clock = new SettableClock();
        var sut = new InMemoryOutboxRepository(clock);
        var delayed = CreateEnvelope(deliverAt: clock.UtcNow + ShortDelay);
        await sut.AppendAsync(new[] { delayed });

        await sut.DeleteAsync(delayed);
        clock.UtcNow += LongDelay;

        Assert.Empty(await sut.ReadPendingAsync());
    }

    [Fact]
    public async Task ReadPendingAsync_should_not_release_a_message_at_the_due_time_it_had_before_it_was_deleted_and_appended_again()
    {
        var clock = new SettableClock();
        var sut = new InMemoryOutboxRepository(clock);
        var messageId = Guid.NewGuid().ToString();
        var deleted = CreateEnvelope(messageId, clock.UtcNow + ShortDelay);
        var appendedAgain = CreateEnvelope(messageId, clock.UtcNow + LongDelay);
        await sut.AppendAsync(new[] { deleted });
        await sut.DeleteAsync(deleted);
        Assert.Equal(OutboxAppendResult.Success, await sut.AppendAsync(new[] { appendedAgain }));

        clock.UtcNow += ShortDelay;
        Assert.Empty(await sut.ReadPendingAsync());

        clock.UtcNow += LongDelay;
        Assert.Same(appendedAgain, Assert.Single(await sut.ReadPendingAsync()));
    }

    [Fact]
    public async Task AppendAsync_should_return_duplicate_for_a_delayed_message_whose_id_is_pending()
    {
        var sut = new InMemoryOutboxRepository();
        var messageId = Guid.NewGuid().ToString();

        Assert.Equal(OutboxAppendResult.Success, await sut.AppendAsync(new[] { CreateEnvelope(messageId, deliverAt: null) }));
        Assert.Equal(OutboxAppendResult.Duplicate, await sut.AppendAsync(new[] { CreateEnvelope(messageId, DateTimeOffset.UtcNow + LongDelay) }));
    }

    [Fact]
    public async Task AppendAsync_should_return_duplicate_for_a_message_without_delay_whose_id_is_waiting_to_be_due()
    {
        var sut = new InMemoryOutboxRepository();
        var messageId = Guid.NewGuid().ToString();

        Assert.Equal(OutboxAppendResult.Success, await sut.AppendAsync(new[] { CreateEnvelope(messageId, DateTimeOffset.UtcNow + LongDelay) }));
        Assert.Equal(OutboxAppendResult.Duplicate, await sut.AppendAsync(new[] { CreateEnvelope(messageId, deliverAt: null) }));
        Assert.Empty(await sut.ReadPendingAsync());
    }

    [Fact]
    public async Task AppendAsync_should_accept_a_message_id_once_when_it_is_appended_with_and_without_a_delay_at_the_same_time()
    {
        const int rounds = 2000;
        var sut = new InMemoryOutboxRepository();
        using var start = new Barrier(participantCount: 2);

        for (var round = 0; round < rounds; round++)
        {
            var messageId = Guid.NewGuid().ToString();
            var appends = new[]
            {
                CreateEnvelope(messageId, deliverAt: null),
                CreateEnvelope(messageId, DateTimeOffset.UtcNow + LongDelay)
            }.Select(message => Task.Run(() =>
            {
                start.SignalAndWait();
                return sut.AppendAsync(new[] { message }).AsTask();
            }));

            var results = await Task.WhenAll(appends);

            Assert.Single(results, OutboxAppendResult.Success);
            Assert.Single(results, OutboxAppendResult.Duplicate);
        }
    }

    private static readonly TimeSpan ShortDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongDelay = TimeSpan.FromHours(1);

    private static MessageEnvelope CreateEnvelope(DateTimeOffset? deliverAt)
        => CreateEnvelope(Guid.NewGuid().ToString(), deliverAt);

    private static MessageEnvelope CreateEnvelope(string messageId, DateTimeOffset? deliverAt)
    {
        var serializer = new JsonSerializer();
        var created = MessageEnvelope.TryCreate(
            serializer.Serialize(new DummyMessage()),
            messageId,
            correlationId: Guid.NewGuid().ToString(),
            createdAt: DateTimeOffset.UtcNow,
            typeof(DummyMessage),
            senderId: Guid.NewGuid().ToString(),
            deliverAt,
            serializer,
            out var envelope);
        Assert.True(created);
        return envelope!;
    }

    // the time the repository reads, moved by the test instead of waited for
    private sealed class SettableClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [Fact]
    public async Task AppendAsync_should_return_duplicate_for_a_message_id_that_is_waiting_to_be_due()
    {
        var sut = new InMemoryOutboxRepository();
        var delayed = DummyMessage.CreateEnvelope(delay: TimeSpan.FromHours(1));

        Assert.Equal(OutboxAppendResult.Success, await sut.AppendAsync(new[] { delayed }));
        Assert.Equal(OutboxAppendResult.Duplicate, await sut.AppendAsync(new[] { delayed }));
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_empty_when_no_messages()
    {
        var sut = new InMemoryOutboxRepository();
        var pending = await sut.ReadPendingAsync();
        Assert.Empty(pending);
    }
}
