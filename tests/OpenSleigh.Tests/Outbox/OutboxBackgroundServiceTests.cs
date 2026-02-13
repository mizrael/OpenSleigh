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

        await outboxProcessor.Received().ProcessPendingMessagesAsync(Arg.Any<CancellationToken>());
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
