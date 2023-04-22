using Microsoft.Extensions.Logging;
using NSubstitute;

namespace OpenSleigh.Tests
{
    public class SagaRunnerTests
    {
        [Fact]
        public async Task ProcessAsync_should_do_nothing_when_context_not_found()
        {
            var message = new FakeSagaStarter();
            var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);
            var descriptor = SagaDescriptor.Create<FakeSaga>();

            var logger = Substitute.For<ILogger<SagaRunner>>();
                        
            var executionContext = Substitute.For<ISagaExecutionContext>();

            var sagaExecutionService = Substitute.For<ISagaExecutionService>();
            sagaExecutionService.StartExecutionContextAsync<FakeSagaStarter>(messageContext, descriptor, Arg.Any<CancellationToken>())
                .Returns(executionContext);
            
            var messageHandlerManager = Substitute.For<IMessageHandlerManager>();
            var sut = new SagaRunner(logger, sagaExecutionService, messageHandlerManager);
            
            await sut.ProcessAsync(messageContext, descriptor);

            await sagaExecutionService.Received(1).StartExecutionContextAsync(messageContext, descriptor);
            await messageHandlerManager.DidNotReceiveWithAnyArgs().ProcessAsync(messageContext, null);
            await sagaExecutionService.DidNotReceiveWithAnyArgs().CommitAsync(null);        
        }
    }
}