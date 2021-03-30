using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
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
            var messageHandlersRunner = NSubstitute.Substitute.For<IMessageHandlersRunner>();
            var logger = NSubstitute.Substitute.For<ILogger<MessageProcessor>>();
            
            var sut = new MessageProcessor(runner, messageHandlersRunner, factory, logger);

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ProcessAsync<StartDummySaga>(null));
        }

        [Fact]
        public async Task ProcessAsync_should_run_sagas()
        {
            var message = StartDummySaga.New();
            var runner = NSubstitute.Substitute.For<ISagasRunner>();
            var messageHandlersRunner = NSubstitute.Substitute.For<IMessageHandlersRunner>();

            var ctx = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            var factory = NSubstitute.Substitute.For<IMessageContextFactory>();
            factory.Create(message)
                .Returns(ctx);
            
            var logger = NSubstitute.Substitute.For<ILogger<MessageProcessor>>();

            var sut = new MessageProcessor(runner, messageHandlersRunner, factory, logger);
            await sut.ProcessAsync<StartDummySaga>(message);

            await runner.Received(1).RunAsync(ctx, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessAsync_should_run_message_handlers()
        {
            var message = StartDummySaga.New();
            var sagasRnner = NSubstitute.Substitute.For<ISagasRunner>();
            var messageHandlersRunner = NSubstitute.Substitute.For<IMessageHandlersRunner>();

            var ctx = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            var factory = NSubstitute.Substitute.For<IMessageContextFactory>();
            factory.Create(message)
                .Returns(ctx);
            
            var logger = NSubstitute.Substitute.For<ILogger<MessageProcessor>>();

            var sut = new MessageProcessor(sagasRnner, messageHandlersRunner, factory, logger);
            await sut.ProcessAsync<StartDummySaga>(message);

            await messageHandlersRunner.Received(1).RunAsync(ctx, Arg.Any<CancellationToken>());
        }
    }

}
