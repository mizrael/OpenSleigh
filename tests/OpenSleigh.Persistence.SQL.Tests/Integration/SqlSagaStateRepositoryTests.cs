using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.ComponentModel;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public abstract class SqlSagaStateRepositoryTests
{
    private readonly DbFixture _fixture;

    public SqlSagaStateRepositoryTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FindAsync_should_return_null_if_not_existing()
    {
        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var descriptor = SagaDescriptor.Create<FakeSagaNoState>();
        var messageContext = CreateMessageContext<FakeMessage>();

        var result = await sut.FindAsync(descriptor, messageContext, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_should_return_item_if_existing()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        await sut.LockAsync(sagaContext, CancellationToken.None);

        var result = await sut.FindAsync(sagaContext.Descriptor, messageContext, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(sagaContext.InstanceId, result.InstanceId);
    }

    [Fact]
    public async Task LockAsync_should_lock_item()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var lockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        var lockedState = await db.SagaStates.FirstOrDefaultAsync(e =>
                                    e.LockId == lockId &&
                                    e.InstanceId == sagaContext.InstanceId &&
                                    e.CorrelationId == sagaContext.CorrelationId);
        Assert.NotNull(lockedState);
    }

    [Fact]
    public async Task LockAsync_should_throw_if_item_already_locked()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var lockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(sagaContext, CancellationToken.None));
        Assert.Contains($"saga state '{sagaContext.InstanceId}' is already locked", ex.Message);
    }

    [Fact]
    public async Task LockAsync_should_lock_again_if_first_lock_expired()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var firstLockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        await Task.Delay(500);

        var messageContext2 = CreateMessageContext<FakeMessage>();
        var secondLockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        Assert.NotNull(secondLockId);
        Assert.NotEqual(firstLockId, secondLockId);
    }

    [Fact]
    public async Task LockAsync_should_handle_multiple_saga_types_with_same_correlation_id()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        // Use the same correlation ID for both sagas
        var sharedCorrelationId = Guid.NewGuid().ToString();

        // Create and lock first saga type (FakeSagaNoState) with shared correlation ID
        var messageContext1 = NSubstitute.Substitute.For<IMessageContext<FakeMessage>>();
        messageContext1.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext1.CorrelationId.Returns(sharedCorrelationId);

        var descriptor1 = SagaDescriptor.Create<FakeSagaNoState>();
        var factory = new SagaInstanceFactory();
        var saga1 = factory.Create(descriptor1, messageContext1);

        var lockId1 = await sut.LockAsync(saga1, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(lockId1));

        // Create and lock second saga type (FakeSagaWithState) with SAME correlation ID
        var messageContext2 = NSubstitute.Substitute.For<IMessageContext<FakeMessage>>();
        messageContext2.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext2.CorrelationId.Returns(sharedCorrelationId);

        var descriptor2 = SagaDescriptor.Create<FakeSagaWithState>();
        var saga2 = factory.Create(descriptor2, messageContext2);

        // This should succeed - different saga types can share correlation IDs
        // But will fail with current implementation because line 96 doesn't filter by saga type
        var lockId2 = await sut.LockAsync(saga2, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(lockId2));

        // Verify both sagas were created with different instance IDs
        Assert.NotEqual(saga1.InstanceId, saga2.InstanceId);

        // Verify both sagas exist in database with same correlation ID but different saga types
        var saga1Entity = await db.SagaStates.FirstOrDefaultAsync(e => e.InstanceId == saga1.InstanceId);
        var saga2Entity = await db.SagaStates.FirstOrDefaultAsync(e => e.InstanceId == saga2.InstanceId);

        Assert.NotNull(saga1Entity);
        Assert.NotNull(saga2Entity);
        Assert.Equal(sharedCorrelationId, saga1Entity!.CorrelationId);
        Assert.Equal(sharedCorrelationId, saga2Entity!.CorrelationId);
        Assert.NotEqual(saga1Entity.SagaType, saga2Entity.SagaType);
    }

    [Fact]
    public async Task ReleaseLockAsync_should_throw_when_state_not_found()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await sut.ReleaseAsync(sagaContext));
        Assert.Contains($"saga state '{sagaContext.InstanceId}' not found", ex.Message);
    }

    [Fact]
    public async Task ReleaseLockAsync_should_throw_when_lock_invalid()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        await sut.LockAsync(sagaContext, CancellationToken.None);

        var fakeContext = NSubstitute.Substitute.For<ISagaInstance >();
        fakeContext.InstanceId.Returns(sagaContext.InstanceId);
        fakeContext.LockId.Returns("lorem");

        var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseAsync(fakeContext));
        Assert.Contains($"unable to release Saga State '{sagaContext.InstanceId}' with lock id 'lorem'", ex.Message);
    }

    [Fact]
    public async Task ReleaseLockAsync_should_release_lock_and_update_state()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var messageContext2 = CreateMessageContext<FakeMessage>();

        await sagaContext.LockAsync(sut, CancellationToken.None);

        sagaContext.SetAsProcessed(messageContext);
        sagaContext.SetAsProcessed(messageContext2);

        sagaContext.MarkAsCompleted();

        await sut.ReleaseAsync(sagaContext);

        var unLockedState = await db.SagaStates.FirstOrDefaultAsync(e => e.InstanceId == sagaContext.InstanceId);
        Assert.NotNull(unLockedState);
        Assert.Null(unLockedState.LockId);
        Assert.Null(unLockedState.LockTime);
        Assert.True(unLockedState.IsCompleted);
        Assert.NotNull(unLockedState.ProcessedMessages);
        Assert.NotEmpty(unLockedState.ProcessedMessages);
        Assert.Equal(2, unLockedState.ProcessedMessages.Count);
        Assert.Contains(unLockedState.ProcessedMessages, m => m.MessageId == messageContext.MessageId);
        Assert.Contains(unLockedState.ProcessedMessages, m => m.MessageId == messageContext2.MessageId);
    }

    [Fact]
    public async Task ReleaseAsync_should_throw_and_write_nothing_when_the_lock_expired_and_was_taken_over_and_released()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var dbName = Guid.NewGuid().ToString();
        var (firstOwnerDb, _) = _fixture.CreateDbContext(dbName);
        var (secondOwnerDb, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        var firstOwnerRepository = CreateSut(firstOwnerDb, options);
        var secondOwnerRepository = CreateSut(secondOwnerDb, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var firstOwner = CreateSagaContext(messageContext);
        await firstOwner.LockAsync(firstOwnerRepository, CancellationToken.None);

        var secondOwner = CreateSameSaga(firstOwner);
        await secondOwner.LockAsync(secondOwnerRepository, CancellationToken.None);
        await secondOwnerRepository.ReleaseAsync(secondOwner);

        firstOwner.SetAsProcessed(messageContext);
        await Assert.ThrowsAsync<LockException>(async () => await firstOwnerRepository.ReleaseAsync(firstOwner));

        var stored = await ReadStoredSagaAsync(verificationDb, firstOwner.InstanceId);
        Assert.Empty(stored.ProcessedMessages);
        Assert.Null(stored.LockId);
    }

    [Fact]
    public async Task ReleaseAsync_should_throw_and_leave_the_lock_alone_when_another_owner_holds_it()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var dbName = Guid.NewGuid().ToString();
        var (firstOwnerDb, _) = _fixture.CreateDbContext(dbName);
        var (secondOwnerDb, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        var firstOwnerRepository = CreateSut(firstOwnerDb, options);
        var secondOwnerRepository = CreateSut(secondOwnerDb, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var firstOwner = CreateSagaContext(messageContext);
        await firstOwner.LockAsync(firstOwnerRepository, CancellationToken.None);

        var secondOwner = CreateSameSaga(firstOwner);
        await secondOwner.LockAsync(secondOwnerRepository, CancellationToken.None);

        firstOwner.SetAsProcessed(messageContext);
        firstOwner.MarkAsCompleted();
        await Assert.ThrowsAsync<LockException>(async () => await firstOwnerRepository.ReleaseAsync(firstOwner));

        var stored = await ReadStoredSagaAsync(verificationDb, firstOwner.InstanceId);
        Assert.Empty(stored.ProcessedMessages);
        Assert.Equal(secondOwner.LockId, stored.LockId);
        Assert.False(stored.IsCompleted);
    }

    [Fact]
    public async Task ReleaseAsync_should_keep_stored_processed_messages_and_add_only_the_new_one()
    {
        var dbName = Guid.NewGuid().ToString();
        var (firstDb, _) = _fixture.CreateDbContext(dbName);
        var (secondDb, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        var firstRepository = CreateSut(firstDb);
        var secondRepository = CreateSut(secondDb);

        var firstMessage = CreateMessageContext<FakeMessage>();
        var saga = CreateSagaContext(firstMessage);
        await saga.LockAsync(firstRepository, CancellationToken.None);
        saga.SetAsProcessed(firstMessage);
        await firstRepository.ReleaseAsync(saga);

        var storedAfterFirstCycle = await ReadStoredSagaAsync(verificationDb, saga.InstanceId);
        var firstMessageStoredAt = Assert.Single(storedAfterFirstCycle.ProcessedMessages).When;

        var correlationId = firstMessage.CorrelationId;
        var secondMessage = CreateMessageContext<FakeMessage>();
        secondMessage.CorrelationId.Returns(correlationId);
        var reloadedSaga = await secondRepository.FindAsync(saga.Descriptor, secondMessage, CancellationToken.None);
        Assert.NotNull(reloadedSaga);
        await reloadedSaga.LockAsync(secondRepository, CancellationToken.None);
        reloadedSaga.SetAsProcessed(secondMessage);
        await secondRepository.ReleaseAsync(reloadedSaga);

        var stored = await ReadStoredSagaAsync(verificationDb, saga.InstanceId);
        Assert.Equal(2, stored.ProcessedMessages.Count);
        Assert.Contains(stored.ProcessedMessages, m => m.MessageId == secondMessage.MessageId);
        var firstMessageRow = Assert.Single(stored.ProcessedMessages, m => m.MessageId == firstMessage.MessageId);
        Assert.Equal(firstMessageStoredAt, firstMessageRow.When);
        Assert.Null(stored.LockId);
    }

    [Fact]
    public async Task CommitAsync_should_store_the_outbox_and_release_the_saga()
    {
        var dbName = Guid.NewGuid().ToString();
        var (db, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var saga = CreateSagaContext(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        var published = Publish(saga);

        await sut.CommitAsync(saga);

        var stored = await ReadStoredSagaAsync(verificationDb, saga.InstanceId);
        Assert.Null(stored.LockId);
        Assert.Single(stored.ProcessedMessages);
        var outboxIds = await verificationDb.OutboxMessages.AsNoTracking().Select(e => e.MessageId).ToListAsync();
        Assert.Equal(new[] { published.MessageId }, outboxIds);
    }

    [Fact]
    public async Task CommitAsync_should_store_no_outbox_message_when_the_lock_was_taken_over()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var dbName = Guid.NewGuid().ToString();
        var (firstOwnerDb, _) = _fixture.CreateDbContext(dbName);
        var (secondOwnerDb, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        var firstOwnerRepository = CreateSut(firstOwnerDb, options);
        var secondOwnerRepository = CreateSut(secondOwnerDb, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var firstOwner = CreateSagaContext(messageContext);
        await firstOwner.LockAsync(firstOwnerRepository, CancellationToken.None);
        var secondOwner = CreateSameSaga(firstOwner);
        await secondOwner.LockAsync(secondOwnerRepository, CancellationToken.None);

        firstOwner.SetAsProcessed(messageContext);
        Publish(firstOwner);
        await Assert.ThrowsAsync<LockException>(async () => await firstOwnerRepository.CommitAsync(firstOwner));

        var stored = await ReadStoredSagaAsync(verificationDb, firstOwner.InstanceId);
        Assert.Equal(secondOwner.LockId, stored.LockId);
        Assert.Empty(stored.ProcessedMessages);
        Assert.False(await verificationDb.OutboxMessages.AnyAsync());
    }

    [Fact]
    public async Task CommitAsync_should_skip_a_message_that_is_already_in_the_outbox_and_store_the_others()
    {
        var dbName = Guid.NewGuid().ToString();
        var (db, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        var sut = CreateSut(db);

        var firstMessage = CreateMessageContext<FakeMessage>();
        var correlationId = firstMessage.CorrelationId;
        var saga = CreateSagaContext(firstMessage);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(firstMessage);
        var alreadyStored = Publish(saga);
        await sut.CommitAsync(saga);
        saga.ClearOutbox();

        var secondMessage = CreateMessageContext<FakeMessage>();
        secondMessage.CorrelationId.Returns(correlationId);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(secondMessage);
        saga.Publish(alreadyStored);
        var notStoredYet = Publish(saga);

        await sut.CommitAsync(saga);

        var stored = await ReadStoredSagaAsync(verificationDb, saga.InstanceId);
        Assert.Null(stored.LockId);
        Assert.Equal(2, stored.ProcessedMessages.Count);
        var outboxIds = await verificationDb.OutboxMessages.AsNoTracking().Select(e => e.MessageId).ToListAsync();
        Assert.Equal(2, outboxIds.Count);
        Assert.Contains(alreadyStored.MessageId, outboxIds);
        Assert.Contains(notStoredYet.MessageId, outboxIds);
    }

    [Fact]
    public async Task ReleaseAsync_should_release_the_saga_after_a_commit_that_did_not_complete()
    {
        var dbName = Guid.NewGuid().ToString();
        var (db, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var saga = CreateSagaContext(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        Publish(saga);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sut.CommitAsync(saga, cancelled.Token));

        // what SagaInstance does when its commit fails
        await sut.ReleaseAsync(saga);

        var stored = await ReadStoredSagaAsync(verificationDb, saga.InstanceId);
        Assert.Null(stored.LockId);
        Assert.False(await verificationDb.OutboxMessages.AnyAsync());
    }

    [Fact]
    public async Task ReleaseAsync_should_not_fail_when_the_processed_message_was_stored_by_someone_else_after_the_saga_was_found()
    {
        var dbName = Guid.NewGuid().ToString();
        var (setupDb, _) = _fixture.CreateDbContext(dbName);
        var (firstDb, _) = _fixture.CreateDbContext(dbName);
        var (secondDb, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        var setupRepository = CreateSut(setupDb);
        var firstRepository = CreateSut(firstDb);
        var secondRepository = CreateSut(secondDb);

        var firstMessage = CreateMessageContext<FakeMessage>();
        var correlationId = firstMessage.CorrelationId;
        var saga = CreateSagaContext(firstMessage);
        await saga.LockAsync(setupRepository, CancellationToken.None);
        saga.SetAsProcessed(firstMessage);
        await setupRepository.ReleaseAsync(saga);

        // the same message is delivered to two consumers, which both find the saga before either locks it
        var duplicatedMessage = CreateMessageContext<FakeMessage>();
        duplicatedMessage.CorrelationId.Returns(correlationId);
        var firstCopy = await firstRepository.FindAsync(saga.Descriptor, duplicatedMessage, CancellationToken.None);
        var secondCopy = await secondRepository.FindAsync(saga.Descriptor, duplicatedMessage, CancellationToken.None);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);

        await firstCopy.LockAsync(firstRepository, CancellationToken.None);
        firstCopy.SetAsProcessed(duplicatedMessage);
        await firstRepository.ReleaseAsync(firstCopy);

        await secondCopy.LockAsync(secondRepository, CancellationToken.None);
        secondCopy.SetAsProcessed(duplicatedMessage);
        await secondRepository.ReleaseAsync(secondCopy);

        var stored = await ReadStoredSagaAsync(verificationDb, saga.InstanceId);
        Assert.Null(stored.LockId);
        Assert.Equal(2, stored.ProcessedMessages.Count);
    }

    [Fact]
    public async Task LockAsync_should_lock_a_saga_found_by_the_same_repository_once()
    {
        var dbName = Guid.NewGuid().ToString();
        var (setupDb, _) = _fixture.CreateDbContext(dbName);
        var (firstDb, _) = _fixture.CreateDbContext(dbName);
        var (secondDb, _) = _fixture.CreateDbContext(dbName);
        var setupRepository = CreateSut(setupDb);
        var firstRepository = CreateSut(firstDb);
        var secondRepository = CreateSut(secondDb);

        var firstMessage = CreateMessageContext<FakeMessage>();
        var saga = CreateSagaContext(firstMessage);
        await saga.LockAsync(setupRepository, CancellationToken.None);
        await setupRepository.ReleaseAsync(saga);

        var firstCopy = await firstRepository.FindAsync(saga.Descriptor, firstMessage, CancellationToken.None);
        var secondCopy = await secondRepository.FindAsync(saga.Descriptor, firstMessage, CancellationToken.None);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);

        await firstCopy.LockAsync(firstRepository, CancellationToken.None);
        await Assert.ThrowsAsync<LockException>(async () => await secondCopy.LockAsync(secondRepository, CancellationToken.None));

        await firstRepository.ReleaseAsync(firstCopy);
        await secondCopy.LockAsync(secondRepository, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(secondCopy.LockId));
    }

    private static OpenSleigh.Outbox.MessageEnvelope Publish(ISagaInstance saga)
    {
        var envelope = OpenSleigh.Outbox.MessageEnvelope.Create(new FakeMessage(), saga);
        saga.Publish(envelope);
        return envelope;
    }

    private static ISagaInstance CreateSameSaga(ISagaInstance saga)
        => new SagaInstance(saga.InstanceId, saga.TriggerMessageId, saga.CorrelationId, saga.Descriptor);

    private static Task<Entities.SagaState> ReadStoredSagaAsync(SagaDbContext db, string instanceId)
        => db.SagaStates
            .Include(e => e.ProcessedMessages)
            .AsNoTracking()
            .SingleAsync(e => e.InstanceId == instanceId);

    private SqlSagaStateRepository CreateSut(SagaDbContext db,
        SqlSagaStateRepositoryOptions options = null)
    {
        var serializer = new JsonSerializer();
        var sut = new SqlSagaStateRepository(db, options ?? SqlSagaStateRepositoryOptions.Default, serializer);
        return sut;
    }

    private IMessageContext<TM> CreateMessageContext<TM>() where TM: IMessage
    {
        var messageContext = NSubstitute.Substitute.For<IMessageContext<TM>>();
        messageContext.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());
        return messageContext;
    }

    private ISagaInstance  CreateSagaContext<TM>(IMessageContext<TM> messageContext)
        where TM : IMessage
    {
        var descriptor = SagaDescriptor.Create<FakeSagaNoState>();

        var factory = new SagaInstanceFactory();
        var context = factory.Create(descriptor, messageContext);

        return context;
    }
}
