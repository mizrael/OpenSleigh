using Microsoft.Extensions.Logging;
using NSubstitute;

namespace OpenSleigh.Tests
{
    public class SagaRunnerTests
    {
        [Fact]
        public async Task ProcessAsync_should_do_nothing_when_context_not_found()
        {
            var logger = Substitute.For<ILogger<SagaRunner>>();
            var sagaExecutionService = Substitute.For<ISagaExecutionService>();
            var messageHandlerManager = Substitute.For<IMessageHandlerManager>();
            var sut = new SagaRunner(logger, sagaExecutionService, messageHandlerManager);

            var message = new FakeSagaStarter();
            var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            await sut.ProcessAsync(messageContext, descriptor);

            await sagaExecutionService.Received(1).BeginAsync(messageContext, descriptor);
            await messageHandlerManager.DidNotReceiveWithAnyArgs().ProcessAsync(messageContext, null);
            await sagaExecutionService.DidNotReceiveWithAnyArgs().CommitAsync(null, messageContext, null);
        
        }
    }
}