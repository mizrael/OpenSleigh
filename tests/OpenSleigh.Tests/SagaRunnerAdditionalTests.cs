using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class SagaRunnerAdditionalTests
{
    [Fact]
    public async Task ProcessAsync_should_call_ProcessAsync_on_instance_and_commit()
    {
        var message = new FakeSagaStarter();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var logger = Substitute.For<ILogger<SagaRunner>>();
        var executionContext = Substitute.For<ISagaInstance>();
        executionContext.InstanceId.Returns("inst-1");

        var sagaExecutionService = Substitute.For<ISagaExecutionService>();
        sagaExecutionService.BeginProcessingAsync<FakeSagaStarter>(messageContext, descriptor, Arg.Any<CancellationToken>())
            .Returns(executionContext);

        var messageHandlerManager = Substitute.For<IMessageHandlerManager>();
        var sut = new SagaRunner(logger, sagaExecutionService, messageHandlerManager);

        await sut.ProcessAsync(messageContext, descriptor);

        await executionContext.Received(1).ProcessAsync(
            messageHandlerManager,
            messageContext,
            sagaExecutionService,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var sagaExecutionService = Substitute.For<ISagaExecutionService>();
        var messageHandlerManager = Substitute.For<IMessageHandlerManager>();

        Assert.Throws<ArgumentNullException>(() => new SagaRunner(null!, sagaExecutionService, messageHandlerManager));
    }

    [Fact]
    public void Ctor_should_throw_when_sagaExecutionService_is_null()
    {
        var logger = Substitute.For<ILogger<SagaRunner>>();
        var messageHandlerManager = Substitute.For<IMessageHandlerManager>();

        Assert.Throws<ArgumentNullException>(() => new SagaRunner(logger, null!, messageHandlerManager));
    }

    [Fact]
    public void Ctor_should_throw_when_messageHandlerManager_is_null()
    {
        var logger = Substitute.For<ILogger<SagaRunner>>();
        var sagaExecutionService = Substitute.For<ISagaExecutionService>();

        Assert.Throws<ArgumentNullException>(() => new SagaRunner(logger, sagaExecutionService, null!));
    }
}
