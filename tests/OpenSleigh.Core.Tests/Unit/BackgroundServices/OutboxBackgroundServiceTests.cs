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
    public class OutboxBackgroundServiceTests
    {
        [Fact]
        public void ctor_should_throw_if_input_null()
        {
            var options = OutboxProcessorOptions.Default;
            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            Assert.Throws<ArgumentNullException>(() => new OutboxBackgroundService(null, options));
            Assert.Throws<ArgumentNullException>(() => new OutboxBackgroundService(scopeFactory, null));
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

            var sut = new OutboxBackgroundService(factory, OutboxProcessorOptions.Default);

            var tokenSource = new CancellationTokenSource();
            await sut.StartAsync(tokenSource.Token);
            await Task.Delay(200);
            await processor.Received()
                .ProcessPendingMessagesAsync(Arg.Any<CancellationToken>());
        }
    }
}