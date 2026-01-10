using FluentAssertions;
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
        await act.Should().ThrowAsync<LockException>();
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
        found.Should().NotBeNull();
        found!.CorrelationId.Should().Be(correlationId);
        found.InstanceId.Should().Be(instance.InstanceId);
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
        var act = async () => await repository.LockAsync(instance, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
