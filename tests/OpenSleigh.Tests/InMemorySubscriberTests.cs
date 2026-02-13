using Microsoft.Extensions.Logging;
using OpenSleigh.InMemory.Messaging;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Threading.Channels;

namespace OpenSleigh.Tests;

public class InMemorySubscriberTests
{
    [Fact]
    public async Task StartAsync_and_StopAsync_should_work()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var messageProcessor = Substitute.For<IMessageProcessor>();
        var logger = Substitute.For<ILogger<InMemorySubscriber>>();
        var publisher = Substitute.For<IPublisher>();

        var sut = new InMemorySubscriber(messageProcessor, channel.Reader, logger, publisher);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_process_messages_from_channel()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var messageProcessor = Substitute.For<IMessageProcessor>();
        var logger = Substitute.For<ILogger<InMemorySubscriber>>();
        var publisher = Substitute.For<IPublisher>();

        var sut = new InMemorySubscriber(messageProcessor, channel.Reader, logger, publisher);

        var envelope = DummyMessage.CreateEnvelope();
        await channel.Writer.WriteAsync(envelope);

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        try { await sut.StopAsync(CancellationToken.None); } catch { }

        await messageProcessor.Received().ProcessAsync(envelope, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Ctor_should_throw_when_messageProcessor_is_null()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var logger = Substitute.For<ILogger<InMemorySubscriber>>();
        var publisher = Substitute.For<IPublisher>();

        Assert.Throws<ArgumentNullException>(
            () => new InMemorySubscriber(null!, channel.Reader, logger, publisher));
    }

    [Fact]
    public void Ctor_should_throw_when_reader_is_null()
    {
        var messageProcessor = Substitute.For<IMessageProcessor>();
        var logger = Substitute.For<ILogger<InMemorySubscriber>>();
        var publisher = Substitute.For<IPublisher>();

        Assert.Throws<ArgumentNullException>(
            () => new InMemorySubscriber(messageProcessor, null!, logger, publisher));
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var messageProcessor = Substitute.For<IMessageProcessor>();
        var publisher = Substitute.For<IPublisher>();

        Assert.Throws<ArgumentNullException>(
            () => new InMemorySubscriber(messageProcessor, channel.Reader, null!, publisher));
    }

    [Fact]
    public void Ctor_should_throw_when_publisher_is_null()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var messageProcessor = Substitute.For<IMessageProcessor>();
        var logger = Substitute.For<ILogger<InMemorySubscriber>>();

        Assert.Throws<ArgumentNullException>(
            () => new InMemorySubscriber(messageProcessor, channel.Reader, logger, null!));
    }

    [Fact]
    public void Dispose_should_not_throw()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var messageProcessor = Substitute.For<IMessageProcessor>();
        var logger = Substitute.For<ILogger<InMemorySubscriber>>();
        var publisher = Substitute.For<IPublisher>();

        var sut = new InMemorySubscriber(messageProcessor, channel.Reader, logger, publisher);
        sut.Dispose();
        sut.Dispose(); // double dispose should not throw
    }
}
