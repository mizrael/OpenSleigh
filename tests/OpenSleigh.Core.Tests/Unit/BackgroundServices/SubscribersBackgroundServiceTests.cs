using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core.BackgroundServices;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.BackgroundServices
{
    public class SubscribersBackgroundServiceTests
    {
        [Fact]
        public void ctor_should_throw_if_input_null()
        {
            var sysInfo = SystemInfo.New();
            var subscribers = new[]
            {
                NSubstitute.Substitute.For<ISubscriber>(),
            };
            var logger = NSubstitute.Substitute.For<ILogger<SubscribersBackgroundService>>();
            Assert.Throws<ArgumentNullException>(() => new SubscribersBackgroundService(null, sysInfo, logger));            
            Assert.Throws<ArgumentNullException>(() => new SubscribersBackgroundService(subscribers, null, logger));
            Assert.Throws<ArgumentNullException>(() => new SubscribersBackgroundService(subscribers, sysInfo, null));
        }

        [Fact]
        public async Task StartAsync_should_start_subscribers()
        {
            var subscribers = new[]
            {
                NSubstitute.Substitute.For<ISubscriber>(),
                NSubstitute.Substitute.For<ISubscriber>(),
                NSubstitute.Substitute.For<ISubscriber>()
            };
            var sysInfo = SystemInfo.New();

            var logger = NSubstitute.Substitute.For<ILogger<SubscribersBackgroundService>>(); 
            
            var sut = new SubscribersBackgroundService(subscribers, sysInfo, logger);
            
            var tokenSource = new CancellationTokenSource();
            await sut.StartAsync(tokenSource.Token);

            foreach (var subscriber in subscribers)
                await subscriber.Received(1)
                    .StartAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StartAsync_should_not_start_subscribers_when_publish_only()
        {
            var subscribers = new[]
            {
                NSubstitute.Substitute.For<ISubscriber>(),
                NSubstitute.Substitute.For<ISubscriber>(),
                NSubstitute.Substitute.For<ISubscriber>()
            };
            var sysInfo = SystemInfo.New();
            sysInfo.PublishOnly = true;

            var logger = NSubstitute.Substitute.For<ILogger<SubscribersBackgroundService>>();

            var sut = new SubscribersBackgroundService(subscribers, sysInfo, logger);

            var tokenSource = new CancellationTokenSource();
            await sut.StartAsync(tokenSource.Token);

            foreach (var subscriber in subscribers)
                await subscriber.DidNotReceiveWithAnyArgs()
                    .StartAsync(Arg.Any<CancellationToken>());
        }
    }
}
