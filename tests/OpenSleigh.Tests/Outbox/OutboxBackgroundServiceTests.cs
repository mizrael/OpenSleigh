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

    private static OutboxBackgroundService CreateSut(IOutboxProcessor outboxProcessor, OutboxProcessorOptions options, IOutboxSignal signal)
    {
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IOutboxProcessor)).Returns(outboxProcessor);
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.ClientId.Returns("client-1");

        return new OutboxBackgroundService(scopeFactory, options, Substitute.For<ILogger<OutboxBackgroundService>>(), systemInfo, signal);
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
