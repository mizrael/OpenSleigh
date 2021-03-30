using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;
using NSubstitute;
using System.Threading;

namespace OpenSleigh.Core.Tests.Unit.Messaging
{
    public class DefaultMessageHandlersRunnerTests
    {
        [Fact]
        public async Task RunAsync_should_do_nothing_when_no_handlers_registered_for_message()
        {
            var messageHandlersResolver = NSubstitute.Substitute.For<IMessageHandlersResolver>();
            var sut = new DefaultMessageHandlersRunner(messageHandlersResolver);

            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            await sut.RunAsync(messageContext);
        }

        [Fact]
        public async Task RunAsync_should_run_registered_handlers()
        {
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();

            var messageHandlers = new[]
            {
                NSubstitute.Substitute.For<IHandleMessage<StartDummySaga>>(),
                NSubstitute.Substitute.For<IHandleMessage<StartDummySaga>>(),
                NSubstitute.Substitute.For<IHandleMessage<StartDummySaga>>()
            };

            var messageHandlersResolver = NSubstitute.Substitute.For<IMessageHandlersResolver>();
            messageHandlersResolver.Resolve<StartDummySaga>()
                .Returns(messageHandlers);

            var sut = new DefaultMessageHandlersRunner(messageHandlersResolver);            
            await sut.RunAsync(messageContext);

            foreach (var handler in messageHandlers)
                await handler.Received(1)
                    .HandleAsync(messageContext, Arg.Any<CancellationToken>());
        }
    }
}
