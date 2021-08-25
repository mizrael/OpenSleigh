using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core.BackgroundServices;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.BackgroundServices
{
    public class OutboxCleanerBackgroundServiceTests
    {
        [Fact]
        public void ctor_should_throw_if_input_null()
        {
            var factory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            var options = OutboxCleanerOptions.Default;
            var sysInfo = SystemInfo.New();
            var logger = NSubstitute.Substitute.For<ILogger<OutboxCleanerBackgroundService>>();

            Assert.Throws<ArgumentNullException>(() => new OutboxCleanerBackgroundService(null, options, logger, sysInfo));
            Assert.Throws<ArgumentNullException>(() => new OutboxCleanerBackgroundService(factory, null, logger, sysInfo));
            Assert.Throws<ArgumentNullException>(() => new OutboxCleanerBackgroundService(factory, options, null, sysInfo));
            Assert.Throws<ArgumentNullException>(() => new OutboxCleanerBackgroundService(factory, options, logger, null));
        }

        [Fact]
        public async Task StartAsync_should_cleanup_messages()
        {
            var cleaner = NSubstitute.Substitute.For<IOutboxCleaner>();
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(IOutboxCleaner))
                .Returns(cleaner);

            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);

            var factory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            factory.CreateScope().Returns(scope);

            var logger = NSubstitute.Substitute.For<ILogger<OutboxCleanerBackgroundService>>();
            var sysInfo = SystemInfo.New();

            var sut = new OutboxCleanerBackgroundService(factory, OutboxCleanerOptions.Default, logger, sysInfo);

            var tokenSource = new CancellationTokenSource();
            await sut.StartAsync(tokenSource.Token);
            await Task.Delay(200);
            await cleaner.Received()
                .RunCleanupAsync(Arg.Any<CancellationToken>());
        }
    }
}