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
}
