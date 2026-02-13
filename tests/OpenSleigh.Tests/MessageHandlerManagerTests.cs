using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class MessageHandlerManagerTests
{
    [Fact]
    public async Task ProcessAsync_should_invoke_handler()
    {
        var handler = Substitute.For<IHandleMessage<FakeSagaStarter>>();
        var factory = Substitute.For<IMessageHandlerFactory>();
        var logger = Substitute.For<ILogger<MessageHandlerManager>>();
        var context = Substitute.For<ISagaInstance>();

        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        factory.Create<FakeSagaStarter>(context).Returns(handler);

        var sut = new MessageHandlerManager(factory, logger);

        await sut.ProcessAsync(context, messageContext, CancellationToken.None);

        await handler.Received(1).HandleAsync(messageContext, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_should_call_rollback_and_throw_SagaException_on_handler_failure()
    {
        var handler = Substitute.For<IHandleMessage<FakeSagaStarter>>();
        var factory = Substitute.For<IMessageHandlerFactory>();
        var logger = Substitute.For<ILogger<MessageHandlerManager>>();
        var context = Substitute.For<ISagaInstance>();
        context.Descriptor.Returns(SagaDescriptor.Create<FakeSaga>());
        context.InstanceId.Returns("instance-1");

        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        factory.Create<FakeSagaStarter>(context).Returns(handler);

        handler.HandleAsync(messageContext, Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("test error"));

        var sut = new MessageHandlerManager(factory, logger);

        var ex = await Assert.ThrowsAsync<SagaException>(
            () => sut.ProcessAsync(context, messageContext, CancellationToken.None).AsTask());

        Assert.Contains("test error", ex.Message);
        await handler.Received(1).RollbackAsync(messageContext, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Ctor_should_throw_when_factory_is_null()
    {
        var logger = Substitute.For<ILogger<MessageHandlerManager>>();
        Assert.Throws<ArgumentNullException>(() => new MessageHandlerManager(null!, logger));
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var factory = Substitute.For<IMessageHandlerFactory>();
        Assert.Throws<ArgumentNullException>(() => new MessageHandlerManager(factory, null!));
    }
}
