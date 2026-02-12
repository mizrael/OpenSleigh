using OpenSleigh.Outbox;

namespace OpenSleigh.Tests;

public class SagaExecutionServiceTests
{
    [Fact]
    public async Task BeginProcessingAsync_should_wrap_retry_exception_when_retry_fails_for_different_reason()
    {
        // Arrange
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

        // Setup: Create returns a saga instance
        sagaInstanceFactory.Create(descriptor, messageContext).Returns(sagaInstance);

        // Setup: FindAsync returns the saga instance for retry
        sagaStateRepository.FindAsync(descriptor, messageContext, Arg.Any<CancellationToken>())
            .Returns(sagaInstance);

        // Setup: First LockAsync throws OptimisticLockException, retry throws different exception (database down)
        var optimisticLockException = new OptimisticLockException();
        var databaseException = new InvalidOperationException("Database connection failed");

        var callCount = 0;
        sagaStateRepository.When(x => x.LockAsync(sagaInstance, Arg.Any<CancellationToken>()))
            .Do(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                    throw optimisticLockException;
                else
                    throw databaseException;
            });

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        // Act & Assert
        // After fix, should throw AggregateException containing both exceptions
        var act = async () => await sut.BeginProcessingAsync(messageContext, descriptor, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AggregateException>(act);

        // Should contain both the original OptimisticLockException and the retry failure exception
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(exception.InnerExceptions, e => e is OptimisticLockException);
        Assert.Contains(exception.InnerExceptions, e => e.Message == "Database connection failed");
        Assert.Contains("Failed to lock saga after optimistic lock conflict", exception.Message);
    }

    [Fact]
    public async Task BeginProcessingAsync_should_succeed_on_retry_when_lock_acquired()
    {
        // Arrange
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

        // Setup: First LockAsync throws OptimisticLockException
        var optimisticLockException = new OptimisticLockException();
        var lockId = Guid.NewGuid().ToString();

        sagaStateRepository.LockAsync(sagaInstance, Arg.Any<CancellationToken>())
            .Returns(
                x => throw optimisticLockException,  // First call throws
                x => lockId);                         // Second call succeeds

        // Setup: FindAsync returns the saga instance for retry
        sagaStateRepository.FindAsync(descriptor, messageContext, Arg.Any<CancellationToken>())
            .Returns(sagaInstance);

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        // Act
        var result = await sut.BeginProcessingAsync(messageContext, descriptor, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sagaInstance.InstanceId, result.InstanceId);
        await sagaStateRepository.Received(2).LockAsync(sagaInstance, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeginProcessingAsync_should_return_noop_and_release_lock_when_CanProcess_false_after_lock()
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

        sagaStateRepository.FindAsync(descriptor, messageContext, Arg.Any<CancellationToken>())
            .Returns(sagaInstance);

        // First CanProcess call (before lock) returns true because message not processed.
        // LockAsync succeeds, then we simulate the TOCTOU condition:
        // another thread processed the message between initial check and lock, so
        // mark the message as processed when LockAsync is called.
        sagaStateRepository.LockAsync(sagaInstance, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                sagaInstance.SetAsProcessed(messageContext);
                return "lock-id";
            });

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        var result = await sut.BeginProcessingAsync(messageContext, descriptor, CancellationToken.None);

        Assert.IsType<NoOpSagaInstance>(result);
        await sagaStateRepository.Received(1).ReleaseAsync(sagaInstance, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseAsync_should_delegate_to_repository()
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

        await sut.ReleaseAsync(sagaInstance, CancellationToken.None);

        await sagaStateRepository.Received(1).ReleaseAsync(sagaInstance, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_should_append_outbox_and_release_lock()
    {
        // Arrange
        var sagaStateRepository = Substitute.For<ISagaStateRepository>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var sagaInstanceFactory = Substitute.For<ISagaInstanceFactory>();

        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var sagaInstance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        // Add message to outbox
        var message = new FakeSagaStarter();
        var envelope = MessageEnvelope.Create(message, sagaInstance);
        sagaInstance.Publish(envelope);

        var sut = new SagaExecutionService(sagaInstanceFactory, sagaStateRepository, outboxRepository);

        // Act
        await sut.CommitAsync(sagaInstance, CancellationToken.None);

        // Assert
        await outboxRepository.Received(1).AppendAsync(Arg.Any<IEnumerable<MessageEnvelope>>(), Arg.Any<CancellationToken>());
        await sagaStateRepository.Received(1).ReleaseAsync(sagaInstance, Arg.Any<CancellationToken>());
    }
}
