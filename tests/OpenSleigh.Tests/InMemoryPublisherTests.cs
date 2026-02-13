using Microsoft.Extensions.Logging;
using OpenSleigh.InMemory.Messaging;
using OpenSleigh.Outbox;
using System.Threading.Channels;

namespace OpenSleigh.Tests;

public class InMemoryPublisherTests
{
    [Fact]
    public async Task PublishAsync_should_write_message_to_channel()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var logger = Substitute.For<ILogger<InMemoryPublisher>>();
        var sut = new InMemoryPublisher(channel.Writer, logger);

        var envelope = DummyMessage.CreateEnvelope();

        await sut.PublishAsync(envelope, CancellationToken.None);

        var result = await channel.Reader.ReadAsync();
        Assert.Same(envelope, result);
    }

    [Fact]
    public async Task PublishAsync_should_throw_when_message_is_null()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var logger = Substitute.For<ILogger<InMemoryPublisher>>();
        var sut = new InMemoryPublisher(channel.Writer, logger);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.PublishAsync(null!, CancellationToken.None).AsTask());
    }

    [Fact]
    public void Ctor_should_throw_when_writer_is_null()
    {
        var logger = Substitute.For<ILogger<InMemoryPublisher>>();
        Assert.Throws<ArgumentNullException>(() => new InMemoryPublisher(null!, logger));
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        Assert.Throws<ArgumentNullException>(() => new InMemoryPublisher(channel.Writer, null!));
    }
}
