using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.Messaging
{
    public class MessageProcessorTests
    {
        [Fact]
        public async Task ProcessAsync_should_throw_if_message_null()
        {
            var runner = NSubstitute.Substitute.For<ISagasRunner>();
            var factory = NSubstitute.Substitute.For<IMessageContextFactory>();
            var sut = new MessageProcessor(runner, factory);

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ProcessAsync<StartDummySaga>(null));
        }

        [Fact]
        public async Task ProcessAsync_should_run_sagas()
        {
            var message = StartDummySaga.New();
            var runner = NSubstitute.Substitute.For<ISagasRunner>();

            var ctx = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            var factory = NSubstitute.Substitute.For<IMessageContextFactory>();
            factory.Create(message)
                .Returns(ctx);
            
            var sut = new MessageProcessor(runner, factory);
            await sut.ProcessAsync<StartDummySaga>(message);

            await runner.Received(1).RunAsync(ctx, Arg.Any<CancellationToken>());
        }
    }
}
