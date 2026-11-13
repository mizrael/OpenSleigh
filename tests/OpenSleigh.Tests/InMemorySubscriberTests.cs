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
    public async Task Should_process_a_message_again_after_the_retry_delay_on_a_lock_conflict_without_keeping_the_others_waiting()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var retryDelay = TimeSpan.FromMilliseconds(400);
        var deadLetters = new InMemoryDeadLetters();
        var conflicting = DummyMessage.CreateEnvelope();
        var other = DummyMessage.CreateEnvelope();
        var attempts = new ConcurrentQueue<(MessageEnvelope Message, DateTimeOffset At)>();
        var processedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageProcessor = Substitute.For<IMessageProcessor>();
        messageProcessor.ProcessAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
                        .Returns(call =>
                        {
                            var message = call.Arg<MessageEnvelope>();
                            attempts.Enqueue((message, DateTimeOffset.UtcNow));
                            if (!ReferenceEquals(message, conflicting))
                                return ValueTask.CompletedTask;

                            if (attempts.Count(a => ReferenceEquals(a.Message, conflicting)) == 1)
                                return ValueTask.FromException(new LockException("locked by someone else"));

                            processedAgain.TrySetResult();
                            return ValueTask.CompletedTask;
                        });
        var publisher = Substitute.For<IPublisher>();
        using var sut = new InMemorySubscriber(
            ScopeFactoryFor(() => messageProcessor),
            channel.Reader,
            Substitute.For<ILogger<InMemorySubscriber>>(),
            publisher,
            InMemorySubscriberOptions.Defaults with { RetryDelay = retryDelay },
            deadLetters);

        await channel.Writer.WriteAsync(conflicting);
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await channel.Writer.WriteAsync(other);
        await processedAgain.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);

        var conflictingAttempts = attempts.Where(a => ReferenceEquals(a.Message, conflicting)).Select(a => a.At).ToArray();
        Assert.Equal(2, conflictingAttempts.Length);
        // timers may fire a few milliseconds early
        Assert.True(conflictingAttempts[1] - conflictingAttempts[0] >= retryDelay - TimeSpan.FromMilliseconds(50),
            $"processed again after {conflictingAttempts[1] - conflictingAttempts[0]}");
        var otherAttempt = Assert.Single(attempts, a => ReferenceEquals(a.Message, other));
        Assert.True(otherAttempt.At < conflictingAttempts[1], "the other message waited for the retry");
        Assert.Empty(deadLetters.Messages);
    }

    [Fact]
    public async Task Should_stop_reading_new_messages_while_too_many_are_waiting_to_be_processed_again()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var conflicts = 0;
        var newMessage = DummyMessage.CreateEnvelope();
        var allConflicted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageProcessor = Substitute.For<IMessageProcessor>();
        messageProcessor.ProcessAsync(Arg.Is<MessageEnvelope>(m => !ReferenceEquals(m, newMessage)), Arg.Any<CancellationToken>())
                        .Returns(_ =>
                        {
                            if (Interlocked.Increment(ref conflicts) == InMemorySubscriber.MaxPendingRetries)
                                allConflicted.TrySetResult();
                            return ValueTask.FromException(new LockException("locked by someone else"));
                        });
        using var sut = new InMemorySubscriber(
            ScopeFactoryFor(() => messageProcessor),
            channel.Reader,
            Substitute.For<ILogger<InMemorySubscriber>>(),
            Substitute.For<IPublisher>(),
            InMemorySubscriberOptions.Defaults with { RetryDelay = TimeSpan.FromHours(1) });

        for (var i = 0; i < InMemorySubscriber.MaxPendingRetries; i++)
            await channel.Writer.WriteAsync(DummyMessage.CreateEnvelope());
        await sut.StartAsync(CancellationToken.None);
        await allConflicted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await channel.Writer.WriteAsync(newMessage);
        await Task.Delay(300);
        await sut.StopAsync(CancellationToken.None);

        await messageProcessor.DidNotReceive().ProcessAsync(newMessage, Arg.Any<CancellationToken>());
        Assert.True(channel.Reader.TryPeek(out var unread));
        Assert.Same(newMessage, unread);
    }

    [Fact]
    public async Task Should_go_on_processing_after_a_message_was_cancelled_by_something_else_than_a_stop()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var deadLetters = new InMemoryDeadLetters();
        var timedOut = DummyMessage.CreateEnvelope();
        var next = DummyMessage.CreateEnvelope();
        var nextProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageProcessor = Substitute.For<IMessageProcessor>();
        messageProcessor.ProcessAsync(timedOut, Arg.Any<CancellationToken>())
                        .Returns(_ => ValueTask.FromException(new TaskCanceledException("the database timed out")));
        messageProcessor.ProcessAsync(next, Arg.Any<CancellationToken>())
                        .Returns(_ =>
                        {
                            nextProcessed.TrySetResult();
                            return ValueTask.CompletedTask;
                        });
        using var sut = new InMemorySubscriber(
            ScopeFactoryFor(() => messageProcessor),
            channel.Reader,
            Substitute.For<ILogger<InMemorySubscriber>>(),
            Substitute.For<IPublisher>(),
            deadLetters: deadLetters);

        await channel.Writer.WriteAsync(timedOut);
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await channel.Writer.WriteAsync(next);
        await nextProcessed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);

        Assert.Same(timedOut, Assert.Single(deadLetters.Messages).Message);
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
    public void UseInMemoryTransport_should_give_the_subscriber_the_batch_size_and_the_retry_delay_of_the_saga_options()
    {
        var services = new ServiceCollection();
        var configurator = Substitute.For<OpenSleigh.DependencyInjection.IBusConfigurator>();
        configurator.Services.Returns(services);

        OpenSleigh.InMemory.InMemoryBusConfiguratorExtensions.UseInMemoryTransport(
            configurator,
            new OpenSleigh.InMemory.InMemorySagaOptions(9) { SubscriberRetryDelay = TimeSpan.FromSeconds(2) });

        var options = Assert.IsType<InMemorySubscriberOptions>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(InMemorySubscriberOptions)).ImplementationInstance);
        Assert.Equal(9, options.MaxMessagesBatchSize);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RetryDelay);
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
