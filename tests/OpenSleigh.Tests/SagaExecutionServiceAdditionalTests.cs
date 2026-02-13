using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class SagaExecutionServiceAdditionalTests
{
    [Fact]
    public void Ctor_should_throw_when_sagaExecCtxFactory_is_null()
    {
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();

        Assert.Throws<ArgumentNullException>(
            () => new SagaExecutionService(null!, sagaStateRepository, outboxRepository));
    }

    [Fact]
    public void Ctor_should_throw_when_sagaStateRepository_is_null()
    {
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();
        var outboxRepository = Substitute.For<IOutboxRepository>();

        Assert.Throws<ArgumentNullException>(
            () => new SagaExecutionService(sagaInstanceFactory, null!, outboxRepository));
    }

    [Fact]
    public void Ctor_should_throw_when_outboxRepository_is_null()
    {
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();

        Assert.Throws<ArgumentNullException>(
            () => new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, null!));
    }

    [Fact]
    public async Task BeginProcessingAsync_should_return_NoOp_when_message_already_processed()
    {
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();

        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        var sagaInstance = new SagaInstance(
            Guid.NewGuid().ToString(),
            messageContext.MessageId,
            messageContext.CorrelationId,
            descriptor);

        // Mark message as already processed
        sagaInstance.SetAsProcessed(messageContext);

        sagaStateRepository.FindAsync(descriptor, messageContext, Arg.Any<CancellationToken>())
            .Returns(sagaInstance);

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        var result = await sut.BeginProcessingAsync(messageContext, descriptor, CancellationToken.None);

        Assert.IsType<NoOpSagaInstance>(result);
    }

    [Fact]
    public async Task BeginProcessingAsync_should_throw_when_no_instance_found_and_not_initiator()
    {
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();

        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaMessage>.Create(new FakeSagaMessage());

        sagaStateRepository.FindAsync(descriptor, messageContext, Arg.Any<CancellationToken>())
            .Returns((ISagaInstance?)null);

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        await Assert.ThrowsAsync<ApplicationException>(
            () => sut.BeginProcessingAsync(messageContext, descriptor, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task BeginProcessingAsync_should_create_instance_for_initiator_message_when_not_found()
    {
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();

        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        sagaStateRepository.FindAsync(descriptor, messageContext, Arg.Any<CancellationToken>())
            .Returns((ISagaInstance?)null);

        var sagaInstance = new SagaInstance(
            Guid.NewGuid().ToString(),
            messageContext.MessageId,
            messageContext.CorrelationId,
            descriptor);

        sagaInstanceFactory.Create(descriptor, messageContext).Returns(sagaInstance);
        sagaStateRepository.LockAsync(sagaInstance, Arg.Any<CancellationToken>())
            .Returns("lock-id");

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        var result = await sut.BeginProcessingAsync(messageContext, descriptor, CancellationToken.None);

        Assert.Same(sagaInstance, result);
        sagaInstanceFactory.Received(1).Create(descriptor, messageContext);
    }

    [Fact]
    public async Task ReleaseAsync_should_throw_when_context_is_null()
    {
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.ReleaseAsync(null!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task CommitAsync_should_throw_when_context_is_null()
    {
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.CommitAsync(null!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task CommitAsync_should_not_append_when_outbox_empty()
    {
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();

        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var sagaInstance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        await sut.CommitAsync(sagaInstance, CancellationToken.None);

        await outboxRepository.DidNotReceive().AppendAsync(Arg.Any<IEnumerable<MessageEnvelope>>(), Arg.Any<CancellationToken>());
        await sagaStateRepository.Received(1).ReleaseAsync(sagaInstance, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeginProcessingAsync_should_throw_LockException_directly()
    {
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();

        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        var sagaInstance = new SagaInstance(
            Guid.NewGuid().ToString(),
            messageContext.MessageId,
            messageContext.CorrelationId,
            descriptor);

        sagaInstanceFactory.Create(descriptor, messageContext).Returns(sagaInstance);
        sagaStateRepository.FindAsync(descriptor, messageContext, Arg.Any<CancellationToken>())
            .Returns((ISagaInstance?)null);

        sagaStateRepository.LockAsync(sagaInstance, Arg.Any<CancellationToken>())
            .Returns<string>(x => throw new LockException("hard lock"));

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        await Assert.ThrowsAsync<LockException>(
            () => sut.BeginProcessingAsync(messageContext, descriptor, CancellationToken.None).AsTask());
    }
}
