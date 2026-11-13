using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSleigh.InMemory.Messaging;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace OpenSleigh.Tests;

public class InMemorySubscriberTests
{
    [Fact]
    public async Task StartAsync_and_StopAsync_should_work()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var sut = CreateSut(channel, Substitute.For<IMessageProcessor>());

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_process_messages_from_channel()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var messageProcessor = Substitute.For<IMessageProcessor>();
        var sut = CreateSut(channel, messageProcessor);

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
    public async Task Should_process_every_message_with_its_own_processor()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var processors = new ConcurrentBag<IMessageProcessor>();
        var scopeFactory = ScopeFactoryFor(() =>
        {
            var processor = Substitute.For<IMessageProcessor>();
            processors.Add(processor);
            return processor;
        });
        var sut = new InMemorySubscriber(
            scopeFactory,
            channel.Reader,
            Substitute.For<ILogger<InMemorySubscriber>>(),
            Substitute.For<IPublisher>());

        var envelopes = new[] { DummyMessage.CreateEnvelope(), DummyMessage.CreateEnvelope(), DummyMessage.CreateEnvelope() };
        foreach (var envelope in envelopes)
            await channel.Writer.WriteAsync(envelope);

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        try { await sut.StopAsync(CancellationToken.None); } catch { }

        Assert.Equal(envelopes.Length, processors.Count);
        Assert.All(processors, processor => Assert.Single(processor.ReceivedCalls()));
    }

    [Fact]
    public void Ctor_should_throw_when_scopeFactory_is_null()
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
        var logger = Substitute.For<ILogger<InMemorySubscriber>>();
        var publisher = Substitute.For<IPublisher>();

        Assert.Throws<ArgumentNullException>(
            () => new InMemorySubscriber(Substitute.For<IServiceScopeFactory>(), null!, logger, publisher));
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var publisher = Substitute.For<IPublisher>();

        Assert.Throws<ArgumentNullException>(
            () => new InMemorySubscriber(Substitute.For<IServiceScopeFactory>(), channel.Reader, null!, publisher));
    }

    [Fact]
    public void Ctor_should_throw_when_publisher_is_null()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var logger = Substitute.For<ILogger<InMemorySubscriber>>();

        Assert.Throws<ArgumentNullException>(
            () => new InMemorySubscriber(Substitute.For<IServiceScopeFactory>(), channel.Reader, logger, null!));
    }

    [Fact]
    public void Dispose_should_not_throw()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var sut = CreateSut(channel, Substitute.For<IMessageProcessor>());

        sut.Dispose();
        sut.Dispose(); // double dispose should not throw
    }

    private static InMemorySubscriber CreateSut(Channel<MessageEnvelope> channel, IMessageProcessor messageProcessor)
        => new(
            ScopeFactoryFor(() => messageProcessor),
            channel.Reader,
            Substitute.For<ILogger<InMemorySubscriber>>(),
            Substitute.For<IPublisher>());

    // a scope per call, each resolving the processor anew, the way the container does for a transient service
    private static IServiceScopeFactory ScopeFactoryFor(Func<IMessageProcessor> createProcessor)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ =>
        {
            var processor = createProcessor();
            var provider = Substitute.For<IServiceProvider>();
            provider.GetService(typeof(IMessageProcessor)).Returns(processor);
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(provider);
            return scope;
        });
        return scopeFactory;
    }
}
