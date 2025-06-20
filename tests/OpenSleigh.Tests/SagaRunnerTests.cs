using Microsoft.Extensions.Logging;

namespace OpenSleigh.Tests;

public class SagaRunnerTests
{
    [Fact]
    public async Task ProcessAsync_should_do_nothing_when_context_not_found()
    {
        var message = new FakeSagaStarter();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var logger = Substitute.For<ILogger<SagaRunner>>();
                    
        var executionContext = Substitute.For<ISagaInstance >();

        var sagaExecutionService = Substitute.For<ISagaExecutionService>();
        sagaExecutionService.BeginInstanceAsync<FakeSagaStarter>(messageContext, descriptor, Arg.Any<CancellationToken>())
            .Returns(executionContext);
        
        var messageHandlerManager = Substitute.For<IMessageHandlerManager>();
        var sut = new SagaRunner(logger, sagaExecutionService, messageHandlerManager);
        
        await sut.ProcessAsync(messageContext, descriptor);

        await sagaExecutionService.Received(1).BeginInstanceAsync(messageContext, descriptor);
        await messageHandlerManager.DidNotReceiveWithAnyArgs().ProcessAsync(null, messageContext);
        await sagaExecutionService.DidNotReceiveWithAnyArgs().CommitAsync(null);        
    }
}
