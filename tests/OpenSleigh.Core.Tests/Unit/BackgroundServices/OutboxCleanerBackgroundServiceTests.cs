using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
            Assert.Throws<ArgumentNullException>(() => new OutboxCleanerBackgroundService(factory, null));
            Assert.Throws<ArgumentNullException>(() => new OutboxCleanerBackgroundService(null, options));
        }

        [Fact]
        public async Task StartAsync_should_start_subscribers()
        {
            var cleaner = NSubstitute.Substitute.For<IOutboxCleaner>();
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(IOutboxCleaner))
                .Returns(cleaner);

            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);

            var factory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            factory.CreateScope().Returns(scope);

            var sut = new OutboxCleanerBackgroundService(factory, OutboxCleanerOptions.Default);

            var tokenSource = new CancellationTokenSource();
            await sut.StartAsync(tokenSource.Token);

            await cleaner.Received(1)
                .RunCleanupAsync(Arg.Any<CancellationToken>());
        }
    }
}