using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests.Transport;

public class MessageDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_should_create_context_and_call_runner()
    {
        var message = new FakeSagaStarter();
        var sagaInstance = Substitute.For<ISagaInstance>();
        sagaInstance.CorrelationId.Returns(Guid.NewGuid().ToString());
        sagaInstance.InstanceId.Returns(Guid.NewGuid().ToString());
        var envelope = MessageEnvelope.Create(message, sagaInstance);
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var runner = Substitute.For<ISagaRunner>();

        var sut = new MessageDispatcher<FakeSagaStarter>();

        await sut.DispatchAsync(envelope, runner, new[] { descriptor }, CancellationToken.None);

        await runner.Received(1).ProcessAsync(
            Arg.Is<IMessageContext<FakeSagaStarter>>(ctx =>
                ctx.MessageId == envelope.MessageId &&
                ctx.CorrelationId == envelope.CorrelationId),
            descriptor,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_should_let_every_saga_handle_the_message_and_then_report_the_one_that_failed()
    {
        var envelope = CreateEnvelope();
        var failing = SagaDescriptor.Create<FakeSaga>();
        var working = SagaDescriptor.Create<FakeSagaWithState>();
        var failure = new SagaException("handler failed", new InvalidOperationException("whoops"));
        var runner = Substitute.For<ISagaRunner>();
        runner.ProcessAsync(Arg.Any<IMessageContext<FakeSagaStarter>>(), failing, Arg.Any<CancellationToken>())
              .Returns(_ => ValueTask.FromException(failure));

        var sut = new MessageDispatcher<FakeSagaStarter>();

        var thrown = await Assert.ThrowsAsync<SagaException>(
            () => sut.DispatchAsync(envelope, runner, new[] { failing, working }, CancellationToken.None).AsTask());

        Assert.Same(failure, thrown);
        await runner.Received(1).ProcessAsync(Arg.Any<IMessageContext<FakeSagaStarter>>(), working, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_should_report_every_saga_that_failed_as_a_failure_that_is_not_a_lock_conflict()
    {
        var envelope = CreateEnvelope();
        var descriptors = new[] { SagaDescriptor.Create<FakeSaga>(), SagaDescriptor.Create<FakeSagaWithState>() };
        var runner = Substitute.For<ISagaRunner>();
        runner.ProcessAsync(Arg.Any<IMessageContext<FakeSagaStarter>>(), Arg.Any<SagaDescriptor>(), Arg.Any<CancellationToken>())
              .Returns(_ => ValueTask.FromException(new SagaException("handler failed", new LockException("thrown by the handler"))));

        var sut = new MessageDispatcher<FakeSagaStarter>();

        var thrown = await Assert.ThrowsAsync<AggregateException>(
            () => sut.DispatchAsync(envelope, runner, descriptors, CancellationToken.None).AsTask());

        Assert.Equal(2, thrown.InnerExceptions.Count);
        Assert.False(thrown.IsLockConflict());
    }

    [Fact]
    public async Task DispatchAsync_should_stop_at_a_failure_that_is_not_a_handler_failure()
    {
        var envelope = CreateEnvelope();
        var locked = SagaDescriptor.Create<FakeSaga>();
        var notReached = SagaDescriptor.Create<FakeSagaWithState>();
        var runner = Substitute.For<ISagaRunner>();
        runner.ProcessAsync(Arg.Any<IMessageContext<FakeSagaStarter>>(), locked, Arg.Any<CancellationToken>())
              .Returns(_ => ValueTask.FromException(new LockException("locked by someone else")));

        var sut = new MessageDispatcher<FakeSagaStarter>();

        await Assert.ThrowsAsync<LockException>(
            () => sut.DispatchAsync(envelope, runner, new[] { locked, notReached }, CancellationToken.None).AsTask());

        await runner.DidNotReceive().ProcessAsync(Arg.Any<IMessageContext<FakeSagaStarter>>(), notReached, Arg.Any<CancellationToken>());
    }

    private static MessageEnvelope CreateEnvelope()
    {
        var sagaInstance = Substitute.For<ISagaInstance>();
        sagaInstance.CorrelationId.Returns(Guid.NewGuid().ToString());
        sagaInstance.InstanceId.Returns(Guid.NewGuid().ToString());
        return MessageEnvelope.Create(new FakeSagaStarter(), sagaInstance);
    }
}
