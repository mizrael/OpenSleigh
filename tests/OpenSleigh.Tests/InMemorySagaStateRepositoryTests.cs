using OpenSleigh.InMemory;
using OpenSleigh.Queries;
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

    public class CounterState
    {
        public int Value { get; set; }
    }

    private class CounterSaga : Saga<CounterState>, IStartedBy<FakeSagaStarter>
    {
        public CounterSaga(ISagaInstance<CounterState> context) : base(context) { }

        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private static readonly SagaDescriptor CounterDescriptor = SagaDescriptor.Create<CounterSaga, CounterState>();

    private static async Task<(ISagaInstance<CounterState> Saga, FakeMessageContext<FakeSagaStarter> Message)> StoreCounterAsync(
        InMemorySagaStateRepository repository, int value)
    {
        var message = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var saga = new SagaInstance<CounterState>(
            Guid.NewGuid().ToString(), message.MessageId, message.CorrelationId, CounterDescriptor, new CounterState { Value = value });

        await saga.LockAsync(repository, CancellationToken.None);
        await repository.ReleaseAsync(saga);
        return (saga, message);
    }

    private static async Task<ISagaInstance<CounterState>> FindCounterAsync(
        InMemorySagaStateRepository repository, FakeMessageContext<FakeSagaStarter> message)
        => (ISagaInstance<CounterState>)(await repository.FindAsync(CounterDescriptor, message))!;

    [Fact]
    public async Task FindAsync_should_return_an_instance_of_its_own_to_every_caller()
    {
        var repository = new InMemorySagaStateRepository();
        var (stored, message) = await StoreCounterAsync(repository, value: 1);

        var first = await FindCounterAsync(repository, message);
        var second = await FindCounterAsync(repository, message);
        first.State.Value = 42;

        Assert.NotSame(stored, first);
        Assert.NotSame(first, second);
        Assert.Equal(1, second.State.Value);
        Assert.Equal(1, (await FindCounterAsync(repository, message)).State.Value);
    }

    [Fact]
    public async Task ReleaseAsync_should_store_what_the_instance_holds()
    {
        var repository = new InMemorySagaStateRepository();
        var (_, message) = await StoreCounterAsync(repository, value: 1);

        var found = await FindCounterAsync(repository, message);
        await found.LockAsync(repository, CancellationToken.None);
        found.State.Value = 2;
        found.SetAsProcessed(message);
        found.MarkAsCompleted();
        await repository.ReleaseAsync(found);

        var released = await FindCounterAsync(repository, message);
        Assert.Equal(2, released.State.Value);
        Assert.True(released.IsCompleted);
        Assert.Equal(message.MessageId, Assert.Single(released.ProcessedMessages).MessageId);
    }

    [Fact]
    public async Task AbandonAsync_should_unlock_the_saga_and_leave_it_as_it_was_stored()
    {
        var repository = new InMemorySagaStateRepository();
        var (_, message) = await StoreCounterAsync(repository, value: 1);

        var found = await FindCounterAsync(repository, message);
        await found.LockAsync(repository, CancellationToken.None);
        found.State.Value = 2;
        found.SetAsProcessed(message);
        found.MarkAsCompleted();
        await repository.AbandonAsync(found);

        var abandoned = await FindCounterAsync(repository, message);
        Assert.Equal(1, abandoned.State.Value);
        Assert.False(abandoned.IsCompleted);
        Assert.Empty(abandoned.ProcessedMessages);
        await abandoned.LockAsync(repository, CancellationToken.None);
    }

    [Fact]
    public async Task AbandonAsync_should_leave_a_saga_whose_first_message_failed_with_a_new_state()
    {
        var repository = new InMemorySagaStateRepository();
        var message = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
        var saga = new SagaInstance<CounterState>("instance-1", message.MessageId, message.CorrelationId, CounterDescriptor, new CounterState());

        await saga.LockAsync(repository, CancellationToken.None);
        saga.State.Value = 7;
        await repository.AbandonAsync(saga);

        var abandoned = await FindCounterAsync(repository, message);
        Assert.Equal("instance-1", abandoned.InstanceId);
        Assert.Equal(0, abandoned.State.Value);
        Assert.Empty(abandoned.ProcessedMessages);
    }

    [Fact]
    public async Task LockAsync_should_refuse_an_instance_found_before_the_saga_was_released_by_someone_else()
    {
        var repository = new InMemorySagaStateRepository();
        var (_, message) = await StoreCounterAsync(repository, value: 1);
        var late = await FindCounterAsync(repository, message);

        var first = await FindCounterAsync(repository, message);
        await first.LockAsync(repository, CancellationToken.None);
        first.State.Value = 2;
        await repository.ReleaseAsync(first);

        await Assert.ThrowsAsync<OptimisticLockException>(
            () => repository.LockAsync(late, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task LockAsync_should_accept_an_instance_found_before_the_saga_was_abandoned_by_someone_else()
    {
        var repository = new InMemorySagaStateRepository();
        var (_, message) = await StoreCounterAsync(repository, value: 1);
        var late = await FindCounterAsync(repository, message);

        var first = await FindCounterAsync(repository, message);
        await first.LockAsync(repository, CancellationToken.None);
        await repository.AbandonAsync(first);

        await repository.LockAsync(late, CancellationToken.None);
    }

    [Fact]
    public async Task LockAsync_should_refuse_a_new_saga_for_a_correlation_that_already_has_one()
    {
        var repository = new InMemorySagaStateRepository();
        var (_, message) = await StoreCounterAsync(repository, value: 1);
        var another = new SagaInstance<CounterState>("another", message.MessageId, message.CorrelationId, CounterDescriptor, new CounterState());

        await Assert.ThrowsAsync<OptimisticLockException>(
            () => repository.LockAsync(another, CancellationToken.None).AsTask());
    }

    private static SagaMessageFailure CreateFailure(
        ISagaInstance saga,
        IMessageContext<FakeSagaStarter> message,
        FailureDecision decision = FailureDecision.DeadLettered)
        => SagaMessageFailure.Create(saga, message, new InvalidOperationException("whoops"), attempt: 1, decision);

    [Fact]
    public async Task Release_and_abandon_should_store_the_failure_of_the_instance()
    {
        var repository = new InMemorySagaStateRepository();
        var (_, message) = await StoreCounterAsync(repository, value: 1);

        var compensated = await FindCounterAsync(repository, message);
        await compensated.LockAsync(repository, CancellationToken.None);
        compensated.RecordFailure(CreateFailure(compensated, message, FailureDecision.Compensated));
        await repository.ReleaseAsync(compensated);

        var deadLettered = await FindCounterAsync(repository, message);
        await deadLettered.LockAsync(repository, CancellationToken.None);
        deadLettered.RecordFailure(CreateFailure(deadLettered, message));
        await repository.AbandonAsync(deadLettered);

        var failures = await repository.GetAllAsync(new SagaFailureQueryFilter { InstanceId = compensated.InstanceId });
        Assert.Equal(2, failures.TotalCount);
        Assert.Equal(
            [FailureDecision.DeadLettered, FailureDecision.Compensated],
            failures.Items.Select(failure => failure.Decision));

        var onlyCompensated = await repository.GetAllAsync(new SagaFailureQueryFilter { Decision = FailureDecision.Compensated });
        Assert.Same(compensated.Failure, Assert.Single(onlyCompensated.Items));
        Assert.Empty((await repository.GetAllAsync(new SagaFailureQueryFilter { InstanceId = "another" })).Items);
    }

    [Fact]
    public async Task Failures_should_be_kept_up_to_a_maximum_and_paged()
    {
        var repository = new InMemorySagaStateRepository();
        var (_, message) = await StoreCounterAsync(repository, value: 1);
        const int extra = 5;

        for (var i = 0; i < InMemorySagaStateRepository.MaxFailures + extra; i++)
        {
            var saga = await FindCounterAsync(repository, message);
            await saga.LockAsync(repository, CancellationToken.None);
            saga.RecordFailure(CreateFailure(saga, message) with { Attempt = i + 1 });
            await repository.AbandonAsync(saga);
        }

        var page = await repository.GetAllAsync(new SagaFailureQueryFilter { Page = 2, PageSize = 10 });

        Assert.Equal(InMemorySagaStateRepository.MaxFailures, page.TotalCount);
        Assert.Equal(10, page.Items.Count);

        // the oldest were dropped
        var all = await repository.GetAllAsync(new SagaFailureQueryFilter { PageSize = InMemorySagaStateRepository.MaxFailures });
        Assert.Equal(extra + 1, all.Items.Min(failure => failure.Attempt));
    }

    [Fact]
    public async Task PurgeAsync_should_remove_the_failures_that_occurred_before_the_given_time()
    {
        var repository = new InMemorySagaStateRepository();
        var (_, message) = await StoreCounterAsync(repository, value: 1);
        var now = DateTimeOffset.UtcNow;

        foreach (var age in new[] { TimeSpan.FromDays(10), TimeSpan.FromDays(5), TimeSpan.Zero })
        {
            var saga = await FindCounterAsync(repository, message);
            await saga.LockAsync(repository, CancellationToken.None);
            saga.RecordFailure(CreateFailure(saga, message) with { OccurredAt = now - age });
            await repository.AbandonAsync(saga);
        }

        var removed = await repository.PurgeAsync(now - TimeSpan.FromDays(7));

        Assert.Equal(1, removed);
        var kept = await repository.GetAllAsync(new SagaFailureQueryFilter());
        Assert.Equal([now, now - TimeSpan.FromDays(5)], kept.Items.Select(failure => failure.OccurredAt));
    }
}
