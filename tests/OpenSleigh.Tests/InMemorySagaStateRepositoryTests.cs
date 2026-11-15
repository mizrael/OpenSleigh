using OpenSleigh.InMemory;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class InMemorySagaStateRepositoryTests
{
    [Fact]
    public async Task LockAsync_should_throw_lock_exception_when_same_instance_already_locked()
    {
        // Arrange
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        // Act - Lock instance successfully
        await repository.LockAsync(instance, CancellationToken.None);

        // Act & Assert - Try to lock same instance again, should throw LockException
        var act = async () => await repository.LockAsync(instance, CancellationToken.None);
        await Assert.ThrowsAsync<LockException>(act);
    }

    [Fact]
    public async Task FindAsync_should_return_saga_by_correlation_id()
    {
        // Arrange
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var correlationId = Guid.NewGuid().ToString();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            correlationId,
            descriptor);

        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(
            new FakeSagaStarter(),
            correlationId: correlationId);

        // Act - Lock instance (which stores it)
        await repository.LockAsync(instance, CancellationToken.None);

        // Assert - Should be able to find it by correlation ID
        var found = await repository.FindAsync(descriptor, messageContext, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(correlationId, found!.CorrelationId);
        Assert.Equal(instance.InstanceId, found.InstanceId);
    }

    [Fact]
    public async Task ReleaseAsync_should_throw_when_lock_id_does_not_match()
    {
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        var sagaStateRepo = Substitute.For<ISagaStateRepository>();

        // Lock via repository to store the lock
        await repository.LockAsync(instance, CancellationToken.None);

        // Simulate a different lock ID on the instance (as if another host took the lock)
        sagaStateRepo.LockAsync(instance, Arg.Any<CancellationToken>()).Returns("wrong-lock-id");
        await instance.LockAsync(sagaStateRepo, CancellationToken.None);

        // Release with mismatched lock ID should throw
        await Assert.ThrowsAsync<LockException>(
            () => repository.ReleaseAsync(instance, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task LockAsync_should_throw_optimistic_lock_when_different_instance_same_descriptor_locked()
    {
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var correlationId = Guid.NewGuid().ToString();

        var instance1 = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            correlationId,
            descriptor);

        var instance2 = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            correlationId,
            descriptor);

        await repository.LockAsync(instance1, CancellationToken.None);

        await Assert.ThrowsAsync<OptimisticLockException>(
            () => repository.LockAsync(instance2, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ReleaseAsync_should_unlock_instance()
    {
        // Arrange
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        // Act - Lock and then release
        await repository.LockAsync(instance, CancellationToken.None);
        await repository.ReleaseAsync(instance, CancellationToken.None);

        // Assert - Should be able to lock again after release
        await repository.LockAsync(instance, CancellationToken.None);
    }

    [Fact]
    public async Task AbandonAsync_should_put_the_saga_back_as_it_was_when_it_was_last_released()
    {
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<CounterSaga, CounterState>();
        var firstMessage = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var secondMessage = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter(), correlationId: firstMessage.CorrelationId);
        var instance = new SagaInstance<CounterState>(
            Guid.NewGuid().ToString(), firstMessage.MessageId, firstMessage.CorrelationId, descriptor, new CounterState { Count = 1 });
        await instance.LockAsync(repository, CancellationToken.None);
        instance.SetAsProcessed(firstMessage);
        await repository.ReleaseAsync(instance, CancellationToken.None);

        // the handler of the second message changes the state and completes the saga, and its commit fails
        await instance.LockAsync(repository, CancellationToken.None);
        instance.State.Count = 2;
        instance.SetAsProcessed(secondMessage);
        instance.MarkAsCompleted();
        await repository.AbandonAsync(instance, CancellationToken.None);

        // the message is delivered again
        var found = await repository.FindAsync(descriptor, secondMessage, CancellationToken.None);
        var foundWithState = Assert.IsAssignableFrom<ISagaInstance<CounterState>>(found);
        Assert.Equal(1, foundWithState.State.Count);
        Assert.False(found.IsCompleted);
        Assert.Equal(firstMessage.MessageId, Assert.Single(found.ProcessedMessages).MessageId);
        Assert.True(found.CanProcess(secondMessage));
        await found.LockAsync(repository, CancellationToken.None);
    }

    [Fact]
    public async Task LockAsync_should_refuse_an_instance_that_was_abandoned_since_it_was_found()
    {
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var instance = new SagaInstance(Guid.NewGuid().ToString(), messageContext.MessageId, messageContext.CorrelationId, descriptor);
        await instance.LockAsync(repository, CancellationToken.None);
        await repository.ReleaseAsync(instance, CancellationToken.None);

        var foundBySomeoneElse = await repository.FindAsync(descriptor, messageContext, CancellationToken.None);
        await instance.LockAsync(repository, CancellationToken.None);
        await repository.AbandonAsync(instance, CancellationToken.None);

        // what they found still has what the abandoned handler did to it
        await Assert.ThrowsAsync<OptimisticLockException>(
            () => repository.LockAsync(foundBySomeoneElse!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AbandonAsync_should_keep_a_saga_that_was_released_with_nothing_handled()
    {
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var instance = new SagaInstance(Guid.NewGuid().ToString(), messageContext.MessageId, messageContext.CorrelationId, descriptor);
        await instance.LockAsync(repository, CancellationToken.None);
        await repository.ReleaseAsync(instance, CancellationToken.None);

        await instance.LockAsync(repository, CancellationToken.None);
        await repository.AbandonAsync(instance, CancellationToken.None);

        var found = await repository.FindAsync(descriptor, messageContext, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Empty(found.ProcessedMessages);
        Assert.False((await repository.GetByInstanceIdAsync(instance.InstanceId))!.IsLocked);
    }

    [Fact]
    public async Task AbandonAsync_should_put_back_what_it_can_of_a_saga_whose_state_cannot_be_serialized()
    {
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<CyclicSaga, CyclicState>();
        var firstMessage = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var secondMessage = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter(), correlationId: firstMessage.CorrelationId);
        var state = new CyclicState();
        var instance = new SagaInstance<CyclicState>(
            Guid.NewGuid().ToString(), firstMessage.MessageId, firstMessage.CorrelationId, descriptor, state);
        await instance.LockAsync(repository, CancellationToken.None);
        instance.SetAsProcessed(firstMessage);
        await repository.ReleaseAsync(instance, CancellationToken.None);

        await instance.LockAsync(repository, CancellationToken.None);
        instance.SetAsProcessed(secondMessage);
        instance.MarkAsCompleted();
        await repository.AbandonAsync(instance, CancellationToken.None);

        var found = await repository.FindAsync(descriptor, secondMessage, CancellationToken.None);
        Assert.Same(state, Assert.IsAssignableFrom<ISagaInstance<CyclicState>>(found).State);
        Assert.False(found.IsCompleted);
        Assert.True(found.CanProcess(secondMessage));
    }

    private sealed class CounterState
    {
        public int Count { get; set; }
    }

    private sealed class CounterSaga(ISagaInstance<CounterState> context) : Saga<CounterState>(context), IStartedBy<FakeSagaStarter>
    {
        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class CyclicState
    {
        public CyclicState Self => this;
    }

    private sealed class CyclicSaga(ISagaInstance<CyclicState> context) : Saga<CyclicState>(context), IStartedBy<FakeSagaStarter>
    {
        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AbandonAsync_should_forget_a_saga_that_was_never_released()
    {
        var repository = new InMemorySagaStateRepository();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            messageContext.MessageId,
            messageContext.CorrelationId,
            descriptor);
        await instance.LockAsync(repository, CancellationToken.None);

        await repository.AbandonAsync(instance, CancellationToken.None);

        // the message that starts the saga is delivered again, and starts it anew
        Assert.Null(await repository.FindAsync(descriptor, messageContext, CancellationToken.None));
        Assert.Null(await repository.GetByInstanceIdAsync(instance.InstanceId));
    }

    [Fact]
    public async Task AbandonAsync_should_throw_when_lock_id_does_not_match()
    {
        var repository = new InMemorySagaStateRepository();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            SagaDescriptor.Create<FakeSaga>());
        await repository.LockAsync(instance, CancellationToken.None);

        var someoneElse = Substitute.For<ISagaStateRepository>();
        someoneElse.LockAsync(instance, Arg.Any<CancellationToken>()).Returns("wrong-lock-id");
        await instance.LockAsync(someoneElse, CancellationToken.None);

        await Assert.ThrowsAsync<LockException>(
            () => repository.AbandonAsync(instance, CancellationToken.None).AsTask());
    }
}
