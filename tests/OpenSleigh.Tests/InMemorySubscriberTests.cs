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
    public async Task Should_publish_a_message_again_after_the_retry_delay_on_a_lock_conflict()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var retryDelay = TimeSpan.FromMilliseconds(300);
        var deadLetters = new InMemoryDeadLetters();
        var failedAt = DateTimeOffset.MaxValue;
        var messageProcessor = Substitute.For<IMessageProcessor>();
        messageProcessor.ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
                        .Returns(_ =>
                        {
                            failedAt = DateTimeOffset.UtcNow;
                            return ValueTask.FromException(new LockException("locked by someone else"));
                        });
        var publishedAt = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = Substitute.For<IPublisher>();
        var envelope = DummyMessage.CreateEnvelope();
        publisher.PublishAsync(envelope, Arg.Any<CancellationToken>())
                 .Returns(_ =>
                 {
                     publishedAt.TrySetResult(DateTimeOffset.UtcNow);
                     return ValueTask.CompletedTask;
                 });
        using var sut = new InMemorySubscriber(
            ScopeFactoryFor(() => messageProcessor),
            channel.Reader,
            Substitute.For<ILogger<InMemorySubscriber>>(),
            publisher,
            InMemorySubscriberOptions.Defaults with { RetryDelay = retryDelay },
            deadLetters);

        await channel.Writer.WriteAsync(envelope);
        await sut.StartAsync(CancellationToken.None);
        var republishedAt = await publishedAt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);

        // timers may fire a few milliseconds early
        Assert.True(republishedAt - failedAt >= retryDelay - TimeSpan.FromMilliseconds(50), $"published again after {republishedAt - failedAt}");
        Assert.Empty(deadLetters.Messages);
    }

    [Fact]
    public async Task Should_dead_letter_a_message_without_publishing_it_again_on_any_other_failure()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var deadLetters = new InMemoryDeadLetters();
        var failure = new InvalidOperationException("whoops");
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageProcessor = Substitute.For<IMessageProcessor>();
        messageProcessor.ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
                        .Returns(_ =>
                        {
                            processed.TrySetResult();
                            return ValueTask.FromException(failure);
                        });
        var publisher = Substitute.For<IPublisher>();
        using var sut = new InMemorySubscriber(
            ScopeFactoryFor(() => messageProcessor),
            channel.Reader,
            Substitute.For<ILogger<InMemorySubscriber>>(),
            publisher,
            InMemorySubscriberOptions.Defaults with { RetryDelay = TimeSpan.Zero },
            deadLetters);

        var envelope = DummyMessage.CreateEnvelope();
        await channel.Writer.WriteAsync(envelope);
        await sut.StartAsync(CancellationToken.None);
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(200);
        await sut.StopAsync(CancellationToken.None);

        var deadLetter = Assert.Single(deadLetters.Messages);
        Assert.Same(envelope, deadLetter.Message);
        Assert.Same(failure, deadLetter.Error);
        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await messageProcessor.Received(1).ProcessAsync(envelope, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dead_letters_should_keep_the_newest_messages_when_full()
    {
        var sut = new InMemoryDeadLetters();
        var failure = new InvalidOperationException("whoops");
        var first = DummyMessage.CreateEnvelope();
        var last = DummyMessage.CreateEnvelope();

        sut.Add(first, failure);
        for (var i = 0; i < InMemoryDeadLetters.Capacity - 1; i++)
            sut.Add(DummyMessage.CreateEnvelope(), failure);
        sut.Add(last, failure);

        var messages = sut.Messages;
        Assert.Equal(InMemoryDeadLetters.Capacity, messages.Count);
        Assert.DoesNotContain(messages, deadLetter => ReferenceEquals(deadLetter.Message, first));
        Assert.Same(last, messages.Last().Message);
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
