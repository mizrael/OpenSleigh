using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using OpenSleigh.Core.BackgroundServices;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.BackgroundServices
{
    public class SubscribersBackgroundServiceTests
    {
        [Fact]
        public void ctor_should_throw_if_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new SubscribersBackgroundService(null));
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

            var sut = new SubscribersBackgroundService(subscribers);
            
            var tokenSource = new CancellationTokenSource();
            await sut.StartAsync(tokenSource.Token);

            foreach (var subscriber in subscribers)
                await subscriber.Received(1)
                    .StartAsync(Arg.Any<CancellationToken>());
        }
    }
}
