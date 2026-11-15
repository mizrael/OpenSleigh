using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

/// <summary>
/// the commit of a saga on the in-memory EF Core provider: no server, so these run everywhere.
/// The same behaviour is checked against PostgreSQL and SQL Server by the integration tests.
/// </summary>
public class SqlSagaStateCommitTests
{
    [Fact]
    public async Task CommitAsync_should_store_the_outbox_and_release_the_saga_together()
    {
        var dbName = Guid.NewGuid().ToString();
        var sut = CreateSut(dbName);
        var messageContext = CreateMessageContext();
        var saga = CreateSaga(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        var published = Publish(saga);

        await sut.CommitAsync(saga);

        await using var db = CreateDbContext(dbName);
        var stored = await db.SagaStates.Include(e => e.ProcessedMessages).SingleAsync();
        Assert.Null(stored.LockTime);
        Assert.Single(stored.ProcessedMessages);
        Assert.Equal(published.MessageId, (await db.OutboxMessages.SingleAsync()).MessageId);
    }

    [Fact]
    public async Task CommitAsync_should_fail_with_a_lock_exception_when_the_lock_was_taken_over()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var firstOwnerRepository = CreateSut(dbName, options);
        var secondOwnerRepository = CreateSut(dbName, options);

        var messageContext = CreateMessageContext();
        var firstOwner = CreateSaga(messageContext);
        await firstOwner.LockAsync(firstOwnerRepository, CancellationToken.None);
        var secondOwner = new SagaInstance(firstOwner.InstanceId, firstOwner.TriggerMessageId, firstOwner.CorrelationId, firstOwner.Descriptor);
        await secondOwner.LockAsync(secondOwnerRepository, CancellationToken.None);

        firstOwner.SetAsProcessed(messageContext);
        Publish(firstOwner);
        await Assert.ThrowsAsync<LockException>(async () => await firstOwnerRepository.CommitAsync(firstOwner));

        // the in-memory provider has no transactions, so "nothing else was stored" is only checked
        // against the real databases, by the integration tests
        await using var db = CreateDbContext(dbName);
        Assert.Equal(secondOwner.LockId, (await db.SagaStates.SingleAsync()).LockId);
    }

    [Fact]
    public async Task AbandonAsync_should_remove_a_saga_whose_first_message_could_not_be_committed()
    {
        var dbName = Guid.NewGuid().ToString();
        var sut = CreateSut(dbName);
        var messageContext = CreateMessageContext();
        var saga = CreateSaga(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        Publish(saga);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sut.CommitAsync(saga, cancelled.Token));

        // what SagaInstance does when its commit fails
        await sut.AbandonAsync(saga);

        // a message that cannot start the saga does not find it
        Assert.Null(await CreateSut(dbName).FindAsync(saga.Descriptor, messageContext));
        await using var db = CreateDbContext(dbName);
        Assert.Empty(await db.SagaStates.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task AbandonAsync_should_keep_a_saga_that_was_released_with_nothing_handled()
    {
        var dbName = Guid.NewGuid().ToString();
        var messageContext = CreateMessageContext();
        var setupRepository = CreateSut(dbName);
        var saga = CreateSaga(messageContext);
        await saga.LockAsync(setupRepository, CancellationToken.None);
        await setupRepository.ReleaseAsync(saga);

        var sut = CreateSut(dbName);
        var found = await sut.FindAsync(saga.Descriptor, messageContext);
        Assert.NotNull(found);
        await found.LockAsync(sut, CancellationToken.None);
        await sut.AbandonAsync(found);

        await using var db = CreateDbContext(dbName);
        Assert.Null((await db.SagaStates.SingleAsync()).LockTime);
    }

    [Fact]
    public async Task AbandonAsync_should_fail_with_a_lock_exception_and_leave_the_saga_alone_when_the_lock_was_taken_over()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var firstOwnerRepository = CreateSut(dbName, options);
        var secondOwnerRepository = CreateSut(dbName, options);
        var firstOwner = CreateSaga(CreateMessageContext());
        await firstOwner.LockAsync(firstOwnerRepository, CancellationToken.None);
        var secondOwner = new SagaInstance(firstOwner.InstanceId, firstOwner.TriggerMessageId, firstOwner.CorrelationId, firstOwner.Descriptor);
        await secondOwner.LockAsync(secondOwnerRepository, CancellationToken.None);

        await Assert.ThrowsAsync<LockException>(async () => await firstOwnerRepository.AbandonAsync(firstOwner));

        await using var db = CreateDbContext(dbName);
        Assert.Equal(secondOwner.LockId, (await db.SagaStates.SingleAsync()).LockId);
    }

    [Fact]
    public async Task AbandonAsync_should_remove_a_saga_it_created_after_another_saga_went_through_the_same_repository()
    {
        var dbName = Guid.NewGuid().ToString();
        var sut = CreateSut(dbName);
        var abandoned = CreateSaga(CreateMessageContext());
        await abandoned.LockAsync(sut, CancellationToken.None);

        var other = CreateSaga(CreateMessageContext());
        await other.LockAsync(sut, CancellationToken.None);
        await sut.ReleaseAsync(other);

        await sut.AbandonAsync(abandoned);

        await using var db = CreateDbContext(dbName);
        Assert.Equal(other.InstanceId, (await db.SagaStates.SingleAsync()).InstanceId);
    }

    [Fact]
    public async Task AbandonAsync_should_fail_with_a_lock_exception_and_leave_the_saga_alone_when_the_instance_holds_no_lock()
    {
        var dbName = Guid.NewGuid().ToString();
        var saga = await StoreSagaAsync(dbName, CreateMessageContext());

        // what a version that released a saga by removing its lock id left behind
        await using (var db = CreateDbContext(dbName))
        {
            (await db.SagaStates.SingleAsync()).LockId = null;
            await db.SaveChangesAsync();
        }

        var neverLocked = new SagaInstance(saga.InstanceId, saga.TriggerMessageId, saga.CorrelationId, saga.Descriptor);
        await Assert.ThrowsAsync<LockException>(async () => await CreateSut(dbName).AbandonAsync(neverLocked));

        await using var verificationDb = CreateDbContext(dbName);
        Assert.Null((await verificationDb.SagaStates.SingleAsync()).LockId);
    }

    [Fact]
    public async Task AbandonAsync_should_release_the_saga_as_it_was_stored()
    {
        var dbName = Guid.NewGuid().ToString();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var firstMessage = CreateMessageContext();
        var state = DummyState.New();
        var setupRepository = CreateSut(dbName);
        var saga = new SagaInstance<DummyState>(Guid.NewGuid().ToString(), firstMessage.MessageId, firstMessage.CorrelationId, descriptor, state);
        await saga.LockAsync(setupRepository, CancellationToken.None);
        saga.SetAsProcessed(firstMessage);
        await setupRepository.ReleaseAsync(saga);

        var sut = CreateSut(dbName, serializer: new SerializerFailingOn<UnstorableMessage>());
        var secondMessage = CreateMessageContext(firstMessage.CorrelationId);
        var found = await sut.FindAsync(descriptor, secondMessage);
        Assert.NotNull(found);
        await found.LockAsync(sut, CancellationToken.None);

        // the handler changed the state and completed the saga
        var changed = new SagaInstance<DummyState>(found.InstanceId, found.TriggerMessageId, found.CorrelationId, descriptor, DummyState.New(), found.ProcessedMessages);
        var lockHolder = Substitute.For<ISagaStateRepository>();
        lockHolder.LockAsync(changed, Arg.Any<CancellationToken>()).Returns(found.LockId);
        await changed.LockAsync(lockHolder, CancellationToken.None);
        changed.SetAsProcessed(secondMessage);
        changed.MarkAsCompleted();
        Publish(changed);
        changed.Publish(MessageEnvelope.Create(new UnstorableMessage(), changed));

        // its commit fails on the second message: by then the saga is changed and released, and the first message added
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.CommitAsync(changed));

        await sut.AbandonAsync(changed);

        var stored = await CreateSut(dbName).FindAsync(descriptor, secondMessage);
        var storedWithState = Assert.IsAssignableFrom<ISagaInstance<DummyState>>(stored);
        Assert.Equal(state, storedWithState.State);
        Assert.False(stored.IsCompleted);
        Assert.Equal(firstMessage.MessageId, Assert.Single(stored.ProcessedMessages).MessageId);
        await using var db = CreateDbContext(dbName);
        Assert.Null((await db.SagaStates.SingleAsync()).LockTime);
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task CommitAsync_should_store_when_a_message_is_due()
    {
        var dbName = Guid.NewGuid().ToString();
        var sut = CreateSut(dbName);
        var saga = CreateSaga(CreateMessageContext());
        await saga.LockAsync(sut, CancellationToken.None);
        var immediate = Publish(saga);
        var delayed = MessageEnvelope.Create(new FakeMessage(), saga, TimeSpan.FromMinutes(5));
        saga.Publish(delayed);

        await sut.CommitAsync(saga);

        await using var db = CreateDbContext(dbName);
        var dueTimes = await db.OutboxMessages.ToDictionaryAsync(e => e.MessageId, e => e.DeliverAt);
        Assert.Null(dueTimes[immediate.MessageId]);
        Assert.Equal(delayed.DeliverAt, dueTimes[delayed.MessageId]);
    }

    [Fact]
    public async Task ReleaseAsync_should_not_store_the_outbox()
    {
        var dbName = Guid.NewGuid().ToString();
        var sut = CreateSut(dbName);
        var saga = CreateSaga(CreateMessageContext());
        await saga.LockAsync(sut, CancellationToken.None);
        Publish(saga);

        await sut.ReleaseAsync(saga);

        await using var db = CreateDbContext(dbName);
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_should_lock_a_saga_found_by_the_same_repository_and_release_should_add_only_the_new_message()
    {
        var dbName = Guid.NewGuid().ToString();
        var firstMessage = CreateMessageContext();
        var correlationId = firstMessage.CorrelationId;
        var saga = await StoreSagaAsync(dbName, firstMessage);

        var sut = CreateSut(dbName);
        var secondMessage = CreateMessageContext(correlationId);
        var found = await sut.FindAsync(saga.Descriptor, secondMessage);
        Assert.NotNull(found);
        await found.LockAsync(sut, CancellationToken.None);

        await using (var db = CreateDbContext(dbName))
            Assert.Equal(found.LockId, (await db.SagaStates.SingleAsync()).LockId);

        found.SetAsProcessed(secondMessage);
        await sut.ReleaseAsync(found);

        await using var verificationDb = CreateDbContext(dbName);
        var stored = await verificationDb.SagaStates.Include(e => e.ProcessedMessages).SingleAsync();
        Assert.Null(stored.LockTime);
        Assert.NotEqual(found.LockId, stored.LockId);
        Assert.Equal(
            new[] { firstMessage.MessageId, secondMessage.MessageId }.Order(),
            stored.ProcessedMessages.Select(m => m.MessageId).Order());
    }

    [Fact]
    public async Task LockAsync_should_throw_when_the_saga_was_locked_by_someone_else_after_it_was_found()
    {
        var dbName = Guid.NewGuid().ToString();
        var firstMessage = CreateMessageContext();
        var saga = await StoreSagaAsync(dbName, firstMessage);

        var sut = CreateSut(dbName);
        var found = await sut.FindAsync(saga.Descriptor, CreateMessageContext(firstMessage.CorrelationId));
        Assert.NotNull(found);

        var someoneElse = CreateSut(dbName);
        await saga.LockAsync(someoneElse, CancellationToken.None);

        await Assert.ThrowsAsync<LockException>(async () => await found.LockAsync(sut, CancellationToken.None));

        await using var db = CreateDbContext(dbName);
        Assert.Equal(saga.LockId, (await db.SagaStates.SingleAsync()).LockId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockAsync_should_not_lock_a_saga_as_it_was_found_when_someone_else_has_locked_and_released_it_since(bool lockedWhenFound)
    {
        var dbName = Guid.NewGuid().ToString();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var firstMessage = CreateMessageContext();
        var correlationId = firstMessage.CorrelationId;
        var setupRepository = CreateSut(dbName);
        var saga = new SagaInstance<DummyState>(Guid.NewGuid().ToString(), firstMessage.MessageId, correlationId, descriptor, DummyState.New());
        await saga.LockAsync(setupRepository, CancellationToken.None);
        saga.SetAsProcessed(firstMessage);
        await setupRepository.ReleaseAsync(saga);

        var sut = CreateSut(dbName);
        var someoneElse = CreateSut(dbName);
        var theirMessage = CreateMessageContext(correlationId);
        var theirs = await someoneElse.FindAsync(descriptor, theirMessage);
        Assert.NotNull(theirs);
        if (lockedWhenFound)
            await theirs.LockAsync(someoneElse, CancellationToken.None);

        var ourMessage = CreateMessageContext(correlationId);
        var found = await sut.FindAsync(descriptor, ourMessage);
        Assert.NotNull(found);

        if (!lockedWhenFound)
            await theirs.LockAsync(someoneElse, CancellationToken.None);
        theirs.SetAsProcessed(theirMessage);
        await someoneElse.ReleaseAsync(theirs);

        // locked as it was found, the saga would be handled without their message and stored over what they stored
        await Assert.ThrowsAsync<OptimisticLockException>(async () => await found.LockAsync(sut, CancellationToken.None));

        // what SagaExecutionService does next: it looks for the saga again, through the same repository
        var foundAgain = await sut.FindAsync(descriptor, ourMessage);
        Assert.NotNull(foundAgain);
        Assert.Contains(foundAgain.ProcessedMessages, m => m.MessageId == theirMessage.MessageId);
        await foundAgain.LockAsync(sut, CancellationToken.None);
        foundAgain.SetAsProcessed(ourMessage);
        await sut.ReleaseAsync(foundAgain);

        await using var db = CreateDbContext(dbName);
        var stored = await db.SagaStates.Include(e => e.ProcessedMessages).SingleAsync();
        Assert.Null(stored.LockTime);
        Assert.Equal(3, stored.ProcessedMessages.Count);
    }

    [Fact]
    public async Task A_repository_should_lock_a_saga_again_after_releasing_it_and_someone_else_handling_it()
    {
        var dbName = Guid.NewGuid().ToString();
        var firstMessage = CreateMessageContext();
        var correlationId = firstMessage.CorrelationId;
        var saga = await StoreSagaAsync(dbName, firstMessage);

        var sut = CreateSut(dbName);
        var secondMessage = CreateMessageContext(correlationId);
        var found = await sut.FindAsync(saga.Descriptor, secondMessage);
        Assert.NotNull(found);
        await found.LockAsync(sut, CancellationToken.None);
        found.SetAsProcessed(secondMessage);
        await sut.ReleaseAsync(found);

        var someoneElse = CreateSut(dbName);
        var theirMessage = CreateMessageContext(correlationId);
        var theirs = await someoneElse.FindAsync(saga.Descriptor, theirMessage);
        Assert.NotNull(theirs);
        await theirs.LockAsync(someoneElse, CancellationToken.None);
        theirs.SetAsProcessed(theirMessage);
        await someoneElse.ReleaseAsync(theirs);

        // the repository is used for another message of the same saga: nothing of the first one is left on it
        var lastMessage = CreateMessageContext(correlationId);
        var foundAgain = await sut.FindAsync(saga.Descriptor, lastMessage);
        Assert.NotNull(foundAgain);
        await foundAgain.LockAsync(sut, CancellationToken.None);
        foundAgain.SetAsProcessed(lastMessage);
        await sut.ReleaseAsync(foundAgain);

        await using var db = CreateDbContext(dbName);
        var stored = await db.SagaStates.Include(e => e.ProcessedMessages).SingleAsync();
        Assert.Null(stored.LockTime);
        Assert.Equal(4, stored.ProcessedMessages.Count);
    }

    [Fact]
    public async Task LockAsync_should_refuse_a_second_lock_and_leave_the_first_one_releasable()
    {
        var dbName = Guid.NewGuid().ToString();
        var firstMessage = CreateMessageContext();
        var saga = await StoreSagaAsync(dbName, firstMessage);

        var sut = CreateSut(dbName);
        var secondMessage = CreateMessageContext(firstMessage.CorrelationId);
        var found = await sut.FindAsync(saga.Descriptor, secondMessage);
        Assert.NotNull(found);
        await found.LockAsync(sut, CancellationToken.None);

        await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(found, CancellationToken.None));

        found.SetAsProcessed(secondMessage);
        await sut.ReleaseAsync(found);

        await using var db = CreateDbContext(dbName);
        var stored = await db.SagaStates.Include(e => e.ProcessedMessages).SingleAsync();
        Assert.Null(stored.LockTime);
        Assert.Equal(2, stored.ProcessedMessages.Count);
    }

    [Fact]
    public async Task FindAsync_should_return_a_new_state_for_a_saga_stored_without_one()
    {
        var dbName = Guid.NewGuid().ToString();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var messageContext = CreateMessageContext();

        // what a version that stored the state with the first release leaves behind while the first message is handled
        await using (var db = CreateDbContext(dbName))
        {
            db.SagaStates.Add(new Entities.SagaState
            {
                InstanceId = Guid.NewGuid().ToString(),
                CorrelationId = messageContext.CorrelationId,
                TriggerMessageId = messageContext.MessageId,
                SagaType = descriptor.SagaType.FullName,
                SagaStateType = descriptor.SagaStateType!.FullName,
                LockId = Guid.NewGuid().ToString(),
                LockTime = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var found = await CreateSut(dbName).FindAsync(descriptor, messageContext);

        var withState = Assert.IsAssignableFrom<ISagaInstance<DummyState>>(found);
        Assert.Equal(new DummyState(), withState.State);
    }

    [Fact]
    public async Task LockAsync_should_store_the_state_of_a_new_saga()
    {
        var dbName = Guid.NewGuid().ToString();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var messageContext = CreateMessageContext();
        var state = DummyState.New();
        var sut = CreateSut(dbName);
        Assert.Null(await sut.FindAsync(descriptor, messageContext));

        var saga = new SagaInstance<DummyState>(Guid.NewGuid().ToString(), messageContext.MessageId, messageContext.CorrelationId, descriptor, state);
        await saga.LockAsync(sut, CancellationToken.None);

        // someone else finds it while its first message is still being handled
        var found = await CreateSut(dbName).FindAsync(descriptor, messageContext);
        var withState = Assert.IsAssignableFrom<ISagaInstance<DummyState>>(found);
        Assert.Equal(state, withState.State);
    }

    [Fact]
    public async Task Lock_and_release_of_a_saga_found_by_the_same_repository_should_keep_its_state()
    {
        var dbName = Guid.NewGuid().ToString();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var firstMessage = CreateMessageContext();
        var state = DummyState.New();

        var firstRepository = CreateSut(dbName);
        var saga = new SagaInstance<DummyState>(Guid.NewGuid().ToString(), firstMessage.MessageId, firstMessage.CorrelationId, descriptor, state);
        await saga.LockAsync(firstRepository, CancellationToken.None);
        saga.SetAsProcessed(firstMessage);
        await firstRepository.ReleaseAsync(saga);

        var secondMessage = CreateMessageContext(firstMessage.CorrelationId);
        var secondRepository = CreateSut(dbName);
        var found = await secondRepository.FindAsync(descriptor, secondMessage);
        Assert.NotNull(found);
        await found.LockAsync(secondRepository, CancellationToken.None);
        found.SetAsProcessed(secondMessage);
        await secondRepository.ReleaseAsync(found);

        var stored = await CreateSut(dbName).FindAsync(descriptor, secondMessage);
        var storedWithState = Assert.IsAssignableFrom<ISagaInstance<DummyState>>(stored);
        Assert.Equal(state, storedWithState.State);
        Assert.Equal(2, stored.ProcessedMessages.Count);
    }

    private static async Task<ISagaInstance> StoreSagaAsync(string dbName, IMessageContext<FakeMessage> messageContext)
    {
        var repository = CreateSut(dbName);
        var saga = CreateSaga(messageContext);
        await saga.LockAsync(repository, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        await repository.ReleaseAsync(saga);
        return saga;
    }

    private static MessageEnvelope Publish(ISagaInstance saga)
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), saga);
        saga.Publish(envelope);
        return envelope;
    }

    private static SqlSagaStateRepository CreateSut(string dbName, SqlSagaStateRepositoryOptions? options = null, ISerializer? serializer = null)
        => new(CreateDbContext(dbName), options ?? SqlSagaStateRepositoryOptions.Default, serializer ?? new JsonSerializer());

    private static SagaDbContext CreateDbContext(string dbName)
        => new(new DbContextOptionsBuilder<SagaDbContext>().UseInMemoryDatabase(dbName).Options);

    private static IMessageContext<FakeMessage> CreateMessageContext(string? correlationId = null)
    {
        var messageContext = Substitute.For<IMessageContext<FakeMessage>>();
        messageContext.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext.CorrelationId.Returns(correlationId ?? Guid.NewGuid().ToString());
        messageContext.Message.Returns(new FakeMessage());
        return messageContext;
    }

    private static ISagaInstance CreateSaga(IMessageContext<FakeMessage> messageContext)
        => new SagaInstanceFactory().Create(SagaDescriptor.Create<FakeSagaNoState>(), messageContext);
}
