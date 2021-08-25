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
    public class OutboxBackgroundServiceTests
    {
        [Fact]
        public void ctor_should_throw_if_input_null()
        {
            var factory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            var options = OutboxProcessorOptions.Default;
            var sysInfo = SystemInfo.New();
            var logger = NSubstitute.Substitute.For<ILogger<OutboxBackgroundService>>();

            Assert.Throws<ArgumentNullException>(() => new OutboxBackgroundService(null, options, logger, sysInfo));
            Assert.Throws<ArgumentNullException>(() => new OutboxBackgroundService(factory, null, logger, sysInfo));
            Assert.Throws<ArgumentNullException>(() => new OutboxBackgroundService(factory, options, null, sysInfo));
            Assert.Throws<ArgumentNullException>(() => new OutboxBackgroundService(factory, options, logger, null));
        }

        [Fact]
        public async Task StartAsync_should_process_pending_messages()
        {
            var processor = NSubstitute.Substitute.For<IOutboxProcessor>();
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(IOutboxProcessor))
                .Returns(processor);

            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);

            var factory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            factory.CreateScope().Returns(scope);

            var logger = NSubstitute.Substitute.For<ILogger<OutboxBackgroundService>>();
            var sysInfo = SystemInfo.New();

            var sut = new OutboxBackgroundService(factory, OutboxProcessorOptions.Default, logger, sysInfo);

            var tokenSource = new CancellationTokenSource();
            await sut.StartAsync(tokenSource.Token);
            await Task.Delay(200);
            await processor.Received()
                .ProcessPendingMessagesAsync(Arg.Any<CancellationToken>());
        }
    }
}