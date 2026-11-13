using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;

namespace OpenSleigh.Tests.Outbox;

public class OutboxBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_should_process_messages_and_stop_on_cancellation()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();

        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxProcessor)).Returns(outboxProcessor);
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var options = new OutboxProcessorOptions(TimeSpan.FromMilliseconds(50));
        var logger = Substitute.For<ILogger<OutboxBackgroundService>>();
        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.ClientId.Returns("client-1");

        var sut = new OutboxBackgroundService(scopeFactory, options, logger, systemInfo);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received().RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_read_again_without_waiting_while_messages_are_relayed()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        outboxProcessor.RelayPendingMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxRelayResult(10, 0), new OutboxRelayResult(10, 0), OutboxRelayResult.Nothing);

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromHours(1)), new OutboxSignal());

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 3);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(3).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_wait_for_the_interval_after_every_read_when_draining_is_disabled()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        outboxProcessor.RelayPendingMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxRelayResult(10, 0));
        var options = new OutboxProcessorOptions(TimeSpan.FromHours(1)) { DrainPendingMessages = false };

        var sut = CreateSut(outboxProcessor, options, new OutboxSignal());

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(1).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_read_again_when_pending_messages_are_signalled()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        var signal = new OutboxSignal();

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromHours(1)), signal);

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);
        signal.NotifyPendingMessages();
        await WaitForCallsAsync(outboxProcessor, 2);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(2).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_ignore_signals_when_wake_on_publish_is_disabled()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        var signal = new OutboxSignal();
        var options = new OutboxProcessorOptions(TimeSpan.FromHours(1)) { WakeOnPublish = false };

        var sut = CreateSut(outboxProcessor, options, signal);

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);
        signal.NotifyPendingMessages();
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(1).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_wait_for_the_interval_when_a_read_only_failed_even_if_signalled()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        outboxProcessor.RelayPendingMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxRelayResult(Relayed: 0, Failed: 3));
        var signal = new OutboxSignal();

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromHours(1)), signal);

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);
        signal.NotifyPendingMessages();
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(1).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_wait_for_the_interval_when_a_read_relayed_some_messages_and_failed_others()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        outboxProcessor.RelayPendingMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxRelayResult(Relayed: 9, Failed: 1));
        var signal = new OutboxSignal();

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromHours(1)), signal);

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);
        signal.NotifyPendingMessages();
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(1).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_read_once_for_any_number_of_signals_raised_before_the_read()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        var signal = new OutboxSignal();
        for (var i = 0; i < 5; i++)
            signal.NotifyPendingMessages();

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromHours(1)), signal);

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(1).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_read_again_when_signalled_during_a_read()
    {
        var signal = new OutboxSignal();
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        var reads = 0;
        outboxProcessor.RelayPendingMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                // a message appended after this read looked at the outbox
                if (Interlocked.Increment(ref reads) == 1)
                    signal.NotifyPendingMessages();
                return OutboxRelayResult.Nothing;
            });

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromHours(1)), signal);

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 2);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(2).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_keep_relaying_after_a_read_throws()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        outboxProcessor.RelayPendingMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("database unreachable"),
                _ => throw new TimeoutException("a timeout"),
                _ => throw new OperationCanceledException("a timeout reported as a cancellation"),
                _ => OutboxRelayResult.Nothing);

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromMilliseconds(10)), new OutboxSignal());

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 4);

        Assert.False(sut.ExecuteTask!.IsCompleted);
        await sut.StopAsync(CancellationToken.None);
        Assert.True(sut.ExecuteTask.IsCompletedSuccessfully);
        Assert.True(outboxProcessor.ReceivedCalls().Count() >= 4);
    }

    [Fact]
    public async Task ExecuteAsync_should_keep_relaying_after_the_processor_cannot_be_resolved()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxProcessor))
            .Returns(_ => throw new InvalidOperationException("database unreachable"), _ => outboxProcessor);

        var sut = CreateSut(serviceProvider, new OutboxProcessorOptions(TimeSpan.FromMilliseconds(10)), new OutboxSignal());

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);

        Assert.False(sut.ExecuteTask!.IsCompleted);
        await sut.StopAsync(CancellationToken.None);
        Assert.True(outboxProcessor.ReceivedCalls().Any());
    }

    [Fact]
    public async Task ExecuteAsync_should_wait_for_the_interval_after_a_read_throws_even_if_signalled()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        outboxProcessor.RelayPendingMessagesAsync(Arg.Any<CancellationToken>())
            .Returns<OutboxRelayResult>(_ => throw new InvalidOperationException("database unreachable"));
        var signal = new OutboxSignal();

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromHours(1)), signal);

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);
        signal.NotifyPendingMessages();
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await outboxProcessor.Received(1).RelayPendingMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_stop_quietly_when_a_read_is_cancelled_by_the_stop()
    {
        var outboxProcessor = Substitute.For<IOutboxProcessor>();
        outboxProcessor.RelayPendingMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(call => UntilCancelledAsync(call.Arg<CancellationToken>()));
        var logger = Substitute.For<ILogger<OutboxBackgroundService>>();

        var sut = CreateSut(outboxProcessor, new OutboxProcessorOptions(TimeSpan.FromHours(1)), new OutboxSignal(), logger);

        await sut.StartAsync(CancellationToken.None);
        await WaitForCallsAsync(outboxProcessor, 1);
        await sut.StopAsync(CancellationToken.None);

        Assert.True(sut.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(logger.ReceivedCalls(), call => call.GetArguments().Contains(LogLevel.Error));
    }

    private static async ValueTask<OutboxRelayResult> UntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return OutboxRelayResult.Nothing;
    }

    private static OutboxBackgroundService CreateSut(
        IOutboxProcessor outboxProcessor,
        OutboxProcessorOptions options,
        IOutboxSignal signal,
        ILogger<OutboxBackgroundService>? logger = null)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxProcessor)).Returns(outboxProcessor);

        return CreateSut(serviceProvider, options, signal, logger);
    }

    private static OutboxBackgroundService CreateSut(
        IServiceProvider serviceProvider,
        OutboxProcessorOptions options,
        IOutboxSignal signal,
        ILogger<OutboxBackgroundService>? logger = null)
    {
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.ClientId.Returns("client-1");

        return new OutboxBackgroundService(
            scopeFactory,
            options,
            logger ?? Substitute.For<ILogger<OutboxBackgroundService>>(),
            systemInfo,
            signal);
    }

    private static async Task WaitForCallsAsync(IOutboxProcessor outboxProcessor, int expectedCalls)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (outboxProcessor.ReceivedCalls().Count() < expectedCalls && DateTime.UtcNow < timeout)
            await Task.Delay(10);
    }

    [Fact]
    public void Ctor_should_throw_when_scopeFactory_is_null()
    {
        var options = OutboxProcessorOptions.Default;
        var logger = Substitute.For<ILogger<OutboxBackgroundService>>();
        var systemInfo = Substitute.For<ISystemInfo>();

        Assert.Throws<ArgumentNullException>(
            () => new OutboxBackgroundService(null!, options, logger, systemInfo));
    }

    [Fact]
    public void Ctor_should_throw_when_options_is_null()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var logger = Substitute.For<ILogger<OutboxBackgroundService>>();
        var systemInfo = Substitute.For<ISystemInfo>();

        Assert.Throws<ArgumentNullException>(
            () => new OutboxBackgroundService(scopeFactory, null!, logger, systemInfo));
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = OutboxProcessorOptions.Default;
        var systemInfo = Substitute.For<ISystemInfo>();

        Assert.Throws<ArgumentNullException>(
            () => new OutboxBackgroundService(scopeFactory, options, null!, systemInfo));
    }

    [Fact]
    public void Ctor_should_throw_when_systemInfo_is_null()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = OutboxProcessorOptions.Default;
        var logger = Substitute.For<ILogger<OutboxBackgroundService>>();

        Assert.Throws<ArgumentNullException>(
            () => new OutboxBackgroundService(scopeFactory, options, logger, null!));
    }
}
