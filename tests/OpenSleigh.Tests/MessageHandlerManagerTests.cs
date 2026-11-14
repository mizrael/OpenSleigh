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
    public async Task ProcessAsync_should_throw_SagaException_on_handler_failure()
    {
        var handler = Substitute.For<IHandleMessage<FakeSagaStarter>>();
        var factory = Substitute.For<IMessageHandlerFactory>();
        var logger = Substitute.For<ILogger<MessageHandlerManager>>();
        var context = Substitute.For<ISagaInstance>();
        context.Descriptor.Returns(SagaDescriptor.Create<FakeSaga>());
        context.InstanceId.Returns("instance-1");

        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        factory.Create<FakeSagaStarter>(context).Returns(handler);

        var handlerError = new InvalidOperationException("test error");
        handler.HandleAsync(messageContext, Arg.Any<CancellationToken>())
            .Returns(x => throw handlerError);

        var sut = new MessageHandlerManager(factory, logger);

        var ex = await Assert.ThrowsAsync<SagaException>(
            () => sut.ProcessAsync(context, messageContext, CancellationToken.None).AsTask());

        Assert.Contains("test error", ex.Message);
        Assert.Same(handlerError, ex.InnerException);
    }

    [Fact]
    public async Task ProcessAsync_should_not_wrap_the_exception_of_a_handler_that_was_interrupted_by_a_stop()
    {
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var saga = new SagaInstance("instance-1", "trigger-1", messageContext.CorrelationId, SagaDescriptor.Create<FakeSaga>());
        using var stopping = new CancellationTokenSource();

        var handler = Substitute.For<IHandleMessage<FakeSagaStarter>>();
        handler.HandleAsync(messageContext, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stopping.Cancel();
                return ValueTask.FromException(new OperationCanceledException(stopping.Token));
            });
        var sut = CreateSut(saga, handler);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.ProcessAsync(saga, messageContext, stopping.Token).AsTask());
    }

    public interface ICompensatingHandler : IHandleMessage<FakeSagaStarter>, ICompensateMessage<FakeSagaStarter>
    {
    }

    [Fact]
    public async Task CompensateAsync_should_invoke_the_compensation_handler_of_the_saga()
    {
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var saga = new SagaInstance("instance-1", "trigger-1", messageContext.CorrelationId, SagaDescriptor.Create<FakeSaga>());
        var handler = Substitute.For<ICompensatingHandler>();
        var failure = new MessageFailure(new InvalidOperationException("whoops"), Attempts: 1);
        var sut = CreateSut(saga, handler);

        await sut.CompensateAsync(saga, messageContext, failure, CancellationToken.None);

        await handler.Received(1).CompensateAsync(messageContext, failure, Arg.Any<CancellationToken>());
        await handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
    }

    [Fact]
    public async Task CompensateAsync_should_throw_when_the_saga_has_no_compensation_handler_for_the_message()
    {
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var saga = new SagaInstance("instance-1", "trigger-1", messageContext.CorrelationId, SagaDescriptor.Create<FakeSaga>());
        var sut = CreateSut(saga, Substitute.For<IHandleMessage<FakeSagaStarter>>());
        var failure = new MessageFailure(new InvalidOperationException("whoops"), Attempts: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CompensateAsync(saga, messageContext, failure, CancellationToken.None).AsTask());
    }

    private static MessageHandlerManager CreateSut(ISagaInstance saga, IHandleMessage<FakeSagaStarter> handler)
    {
        var factory = Substitute.For<IMessageHandlerFactory>();
        factory.Create<FakeSagaStarter>(saga).Returns(handler);
        return new MessageHandlerManager(factory, Substitute.For<ILogger<MessageHandlerManager>>());
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
