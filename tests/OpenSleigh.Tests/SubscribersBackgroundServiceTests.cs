using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class SubscribersBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_should_skip_subscribers_when_publish_only()
    {
        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.PublishOnly.Returns(true);
        systemInfo.ClientId.Returns("client-1");

        var logger = Substitute.For<ILogger<SubscribersBackgroundService>>();
        var subscriber = Substitute.For<IMessageSubscriber>();

        var sut = new SubscribersBackgroundService(systemInfo, logger, new[] { subscriber });

        await sut.StartAsync(CancellationToken.None);
        // Give the background task a moment to run
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await subscriber.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_should_start_subscribers_when_not_publish_only()
    {
        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.PublishOnly.Returns(false);
        systemInfo.ClientId.Returns("client-1");
        systemInfo.ClientGroup.Returns("group-1");

        var logger = Substitute.For<ILogger<SubscribersBackgroundService>>();
        var subscriber = Substitute.For<IMessageSubscriber>();
        subscriber.StartAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);

        var sut = new SubscribersBackgroundService(systemInfo, logger, new[] { subscriber });

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await subscriber.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_should_stop_subscribers_when_not_publish_only()
    {
        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.PublishOnly.Returns(false);
        systemInfo.ClientId.Returns("client-1");
        systemInfo.ClientGroup.Returns("group-1");

        var logger = Substitute.For<ILogger<SubscribersBackgroundService>>();
        var subscriber = Substitute.For<IMessageSubscriber>();
        subscriber.StartAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
        subscriber.StopAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);

        var sut = new SubscribersBackgroundService(systemInfo, logger, new[] { subscriber });

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);

        await subscriber.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Ctor_should_throw_when_systemInfo_is_null()
    {
        var logger = Substitute.For<ILogger<SubscribersBackgroundService>>();
        Assert.Throws<ArgumentNullException>(
            () => new SubscribersBackgroundService(null!, logger, Array.Empty<IMessageSubscriber>()));
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var systemInfo = Substitute.For<ISystemInfo>();
        Assert.Throws<ArgumentNullException>(
            () => new SubscribersBackgroundService(systemInfo, null!, Array.Empty<IMessageSubscriber>()));
    }
}
