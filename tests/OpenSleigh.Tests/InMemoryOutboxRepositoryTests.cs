using OpenSleigh.InMemory.Outbox;
using OpenSleigh.Outbox;

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
    public async Task ReadPendingAsync_should_return_empty_when_no_messages()
    {
        var sut = new InMemoryOutboxRepository();
        var pending = await sut.ReadPendingAsync();
        Assert.Empty(pending);
    }
}
