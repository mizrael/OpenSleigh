using MongoDB.Driver;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using OpenSleigh.Transport;
using System.ComponentModel;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public class MongoSagaStateRepositoryTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public MongoSagaStateRepositoryTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    private MongoSagaStateRepository CreateSut(IDbContext db, MongoSagaStateRepositoryOptions? options = null)
    {
        var serializer = new JsonSerializer();

        var sut = new MongoSagaStateRepository(db, options, serializer);
        return sut;
    }

    private IMessageContext<TM> CreateMessageContext<TM>() where TM : IMessage
    {
        var messageContext = NSubstitute.Substitute.For<IMessageContext<TM>>();
        messageContext.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());
        return messageContext;
    }

    private ISagaInstance CreateSagaContext<TM>(IMessageContext<TM> messageContext)
        where TM : IMessage
    {
        var descriptor = SagaDescriptor.Create<FakeSagaNoState>();

        var factory = new SagaInstanceFactory();
        var context = factory.Create(descriptor, messageContext);

        return context;
    }

    private static OpenSleigh.Outbox.MessageEnvelope Publish(ISagaInstance saga)
    {
        var envelope = OpenSleigh.Outbox.MessageEnvelope.Create(new FakeMessage(), saga);
        saga.Publish(envelope);
        return envelope;
    }

    [Fact]
    public async Task CommitAsync_should_store_the_outbox_ready_to_be_relayed_and_release_the_saga()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var messageContext = CreateMessageContext<FakeMessage>();
        var saga = CreateSagaContext(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        var published = Publish(saga);

        await sut.CommitAsync(saga);

        var storedSaga = await db.SagaStates.Find(e => e.InstanceId == saga.InstanceId).SingleAsync();
        Assert.Null(storedSaga.LockTime);
        Assert.Single(storedSaga.ProcessedMessages);
        var storedMessage = await db.OutboxMessages.Find(e => e.MessageId == published.MessageId).SingleAsync();
        Assert.Null(storedMessage.LockId);
        Assert.Null(storedMessage.LockTime);
    }

    [Fact]
    public async Task A_failed_handler_should_leave_only_the_messages_of_its_rollback_in_the_outbox_ready_to_be_relayed()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var messageContext = CreateMessageContext<FakeMessage>();
        var saga = CreateSagaContext(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);

        var messages = await FailedHandler.ProcessAsync(saga, messageContext, sut);

        var storedSaga = await db.SagaStates.Find(e => e.InstanceId == saga.InstanceId).SingleAsync();
        Assert.Null(storedSaga.LockTime);
        Assert.Equal(messageContext.MessageId, Assert.Single(storedSaga.ProcessedMessages).MessageId);
        var storedMessage = await db.OutboxMessages.Find(e => e.CorrelationId == saga.CorrelationId).SingleAsync();
        Assert.Equal(messages.PublishedByRollback.MessageId, storedMessage.MessageId);
        Assert.Null(storedMessage.LockId);
        Assert.Null(storedMessage.LockTime);
    }

    [Fact]
    public async Task CommitAsync_should_leave_no_outbox_message_when_the_lock_is_not_held()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db, new MongoSagaStateRepositoryOptions(TimeSpan.Zero));
        var messageContext = CreateMessageContext<FakeMessage>();
        var firstOwner = CreateSagaContext(messageContext);
        await firstOwner.LockAsync(sut, CancellationToken.None);
        var secondOwner = new SagaInstance(firstOwner.InstanceId, firstOwner.TriggerMessageId, firstOwner.CorrelationId, firstOwner.Descriptor);
        await secondOwner.LockAsync(sut, CancellationToken.None);

        var published = Publish(firstOwner);
        await Assert.ThrowsAsync<LockException>(async () => await sut.CommitAsync(firstOwner));

        Assert.False(await db.OutboxMessages.Find(e => e.MessageId == published.MessageId).AnyAsync());
        var storedSaga = await db.SagaStates.Find(e => e.InstanceId == firstOwner.InstanceId).SingleAsync();
        Assert.Equal(secondOwner.LockId, storedSaga.LockId);
    }

    [Fact]
    public async Task CommitAsync_should_release_the_saga_when_it_published_nothing()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var saga = CreateSagaContext(CreateMessageContext<FakeMessage>());
        await saga.LockAsync(sut, CancellationToken.None);

        await sut.CommitAsync(saga);

        var storedSaga = await db.SagaStates.Find(e => e.InstanceId == saga.InstanceId).SingleAsync();
        Assert.Null(storedSaga.LockTime);
    }

    [Fact]
    public async Task CommitAsync_should_skip_a_message_that_is_already_in_the_outbox_and_store_the_others()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var saga = CreateSagaContext(CreateMessageContext<FakeMessage>());
        await saga.LockAsync(sut, CancellationToken.None);
        var alreadyStored = Publish(saga);
        await sut.CommitAsync(saga);
        saga.ClearOutbox();

        await saga.LockAsync(sut, CancellationToken.None);
        saga.Publish(alreadyStored);
        var notStoredYet = Publish(saga);
        await sut.CommitAsync(saga);

        Assert.Equal(2, await db.OutboxMessages.CountDocumentsAsync(FilterDefinition<Entities.OutboxMessage>.Empty));
        var storedMessage = await db.OutboxMessages.Find(e => e.MessageId == notStoredYet.MessageId).SingleAsync();
        Assert.Null(storedMessage.LockId);
    }

    [Fact]
    public async Task FindAsync_should_return_null_if_not_existing()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var descriptor = SagaDescriptor.Create<FakeSagaNoState>();
        var messageContext = CreateMessageContext<FakeMessage>();

        var result = await sut.FindAsync(descriptor, messageContext, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_should_return_item_if_existing()
    {
        var db = _fixture.CreateDbContext();
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
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var lockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(e => e.LockId, lockId),
            filterBuilder.Eq(e => e.InstanceId, sagaContext.InstanceId),
            filterBuilder.Eq(e => e.CorrelationId, sagaContext.CorrelationId));
        var lockedState = await db.SagaStates.FindOneAsync(filter);
        Assert.NotNull(lockedState);
    }

    [Fact]
    public async Task LockAsync_should_throw_if_item_already_locked()
    {
        var db = _fixture.CreateDbContext();
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
        var options = new MongoSagaStateRepositoryOptions(TimeSpan.Zero);
        var db = _fixture.CreateDbContext();
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
    public async Task ReleaseLockAsync_should_throw_when_state_not_found()
    {
        var options = new MongoSagaStateRepositoryOptions(TimeSpan.Zero);
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await sut.ReleaseAsync(sagaContext));
        Assert.Contains($"saga state '{sagaContext.InstanceId}' not found", ex.Message);
    }

    [Fact]
    public async Task ReleaseLockAsync_should_throw_when_lock_invalid()
    {
        var options = new MongoSagaStateRepositoryOptions(TimeSpan.Zero);
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        await sut.LockAsync(sagaContext, CancellationToken.None);

        var fakeContext = NSubstitute.Substitute.For<ISagaInstance>();
        fakeContext.InstanceId.Returns(sagaContext.InstanceId);
        fakeContext.LockId.Returns("lorem");
        fakeContext.Descriptor.Returns(sagaContext.Descriptor);

        var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseAsync(fakeContext));
        Assert.Contains($"unable to release Saga State '{sagaContext.InstanceId}' with lock id 'lorem'", ex.Message);
    }

    [Fact]
    public async Task Find_lock_and_release_should_store_a_new_saga_and_keep_its_state_and_messages_on_the_next_message()
    {
        var dbSuffix = Guid.NewGuid().ToString();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var firstMessage = CreateMessageContext<FakeMessage>();
        var correlationId = firstMessage.CorrelationId;
        var state = DummyState.New();

        var firstRepository = CreateSut(_fixture.CreateDbContext(dbSuffix));
        Assert.Null(await firstRepository.FindAsync(descriptor, firstMessage, CancellationToken.None));
        var saga = new SagaInstance<DummyState>(Guid.NewGuid().ToString(), firstMessage.MessageId, correlationId, descriptor, state);
        await saga.LockAsync(firstRepository, CancellationToken.None);
        saga.SetAsProcessed(firstMessage);
        await firstRepository.ReleaseAsync(saga);

        var secondMessage = CreateMessageContext<FakeMessage>();
        secondMessage.CorrelationId.Returns(correlationId);
        var secondRepository = CreateSut(_fixture.CreateDbContext(dbSuffix));
        var found = await secondRepository.FindAsync(descriptor, secondMessage, CancellationToken.None);
        Assert.NotNull(found);
        await found.LockAsync(secondRepository, CancellationToken.None);
        found.SetAsProcessed(secondMessage);
        await secondRepository.ReleaseAsync(found);

        var stored = await CreateSut(_fixture.CreateDbContext(dbSuffix)).FindAsync(descriptor, secondMessage, CancellationToken.None);
        var storedWithState = Assert.IsAssignableFrom<ISagaInstance<DummyState>>(stored);
        Assert.Equal(state, storedWithState.State);
        Assert.Equal(saga.InstanceId, stored.InstanceId);
        Assert.True(string.IsNullOrEmpty(stored.LockId));
        Assert.Equal(
            new[] { firstMessage.MessageId, secondMessage.MessageId }.Order(),
            stored.ProcessedMessages.Select(m => m.MessageId).Order());
    }

    [Fact]
    public async Task LockAsync_should_report_a_saga_that_someone_else_started_after_it_was_not_found_and_work_on_the_next_attempt()
    {
        var dbSuffix = Guid.NewGuid().ToString();
        var firstRepository = CreateSut(_fixture.CreateDbContext(dbSuffix));
        var secondRepository = CreateSut(_fixture.CreateDbContext(dbSuffix));

        // two consumers receive a message that starts the same saga, and neither finds it
        var firstMessage = CreateMessageContext<FakeMessage>();
        var correlationId = firstMessage.CorrelationId;
        var secondMessage = CreateMessageContext<FakeMessage>();
        secondMessage.CorrelationId.Returns(correlationId);
        var descriptor = SagaDescriptor.Create<FakeSagaNoState>();
        Assert.Null(await firstRepository.FindAsync(descriptor, firstMessage, CancellationToken.None));
        Assert.Null(await secondRepository.FindAsync(descriptor, secondMessage, CancellationToken.None));

        var started = CreateSagaContext(firstMessage);
        await started.LockAsync(firstRepository, CancellationToken.None);
        await firstRepository.ReleaseAsync(started);

        var late = CreateSagaContext(secondMessage);
        await Assert.ThrowsAsync<OptimisticLockException>(async () => await late.LockAsync(secondRepository, CancellationToken.None));

        // what SagaExecutionService does next: it looks for the saga again, through the same repository
        var found = await secondRepository.FindAsync(descriptor, secondMessage, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(started.InstanceId, found.InstanceId);
        await found.LockAsync(secondRepository, CancellationToken.None);
        found.SetAsProcessed(secondMessage);
        await secondRepository.ReleaseAsync(found);

        var stored = await firstRepository.FindAsync(descriptor, firstMessage, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Single(stored.ProcessedMessages);
    }

    [Fact]
    public async Task LockAsync_should_throw_when_someone_else_holds_the_lock_and_work_once_it_is_released()
    {
        var dbSuffix = Guid.NewGuid().ToString();
        var ownerRepository = CreateSut(_fixture.CreateDbContext(dbSuffix));
        var sut = CreateSut(_fixture.CreateDbContext(dbSuffix));

        var messageContext = CreateMessageContext<FakeMessage>();
        var saga = CreateSagaContext(messageContext);
        await saga.LockAsync(ownerRepository, CancellationToken.None);

        var found = await sut.FindAsync(saga.Descriptor, messageContext, CancellationToken.None);
        Assert.NotNull(found);
        await Assert.ThrowsAsync<LockException>(async () => await found.LockAsync(sut, CancellationToken.None));

        // once it is released, the saga is not what was found: it has to be looked for again
        await ownerRepository.ReleaseAsync(saga);
        await Assert.ThrowsAsync<OptimisticLockException>(async () => await found.LockAsync(sut, CancellationToken.None));

        var foundAgain = await sut.FindAsync(saga.Descriptor, messageContext, CancellationToken.None);
        Assert.NotNull(foundAgain);
        await foundAgain.LockAsync(sut, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(foundAgain.LockId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockAsync_should_not_lock_a_saga_as_it_was_found_when_someone_else_has_locked_and_released_it_since(bool lockedWhenFound)
    {
        var dbSuffix = Guid.NewGuid().ToString();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var firstMessage = CreateMessageContext<FakeMessage>();
        var correlationId = firstMessage.CorrelationId;
        var setupRepository = CreateSut(_fixture.CreateDbContext(dbSuffix));
        var saga = new SagaInstance<DummyState>(Guid.NewGuid().ToString(), firstMessage.MessageId, correlationId, descriptor, DummyState.New());
        await saga.LockAsync(setupRepository, CancellationToken.None);
        saga.SetAsProcessed(firstMessage);
        await setupRepository.ReleaseAsync(saga);

        var sut = CreateSut(_fixture.CreateDbContext(dbSuffix));
        var someoneElse = CreateSut(_fixture.CreateDbContext(dbSuffix));
        var theirMessage = CreateMessageContext<FakeMessage>();
        theirMessage.CorrelationId.Returns(correlationId);
        var theirs = await someoneElse.FindAsync(descriptor, theirMessage, CancellationToken.None);
        Assert.NotNull(theirs);
        if (lockedWhenFound)
            await theirs.LockAsync(someoneElse, CancellationToken.None);

        var ourMessage = CreateMessageContext<FakeMessage>();
        ourMessage.CorrelationId.Returns(correlationId);
        var found = await sut.FindAsync(descriptor, ourMessage, CancellationToken.None);
        Assert.NotNull(found);

        if (!lockedWhenFound)
            await theirs.LockAsync(someoneElse, CancellationToken.None);
        theirs.SetAsProcessed(theirMessage);
        await someoneElse.ReleaseAsync(theirs);

        // locked as it was found, the saga would be handled without their message and stored over what they stored
        await Assert.ThrowsAsync<OptimisticLockException>(async () => await found.LockAsync(sut, CancellationToken.None));

        // what SagaExecutionService does next: it looks for the saga again, through the same repository
        var foundAgain = await sut.FindAsync(descriptor, ourMessage, CancellationToken.None);
        Assert.NotNull(foundAgain);
        Assert.Contains(foundAgain.ProcessedMessages, m => m.MessageId == theirMessage.MessageId);
        await foundAgain.LockAsync(sut, CancellationToken.None);
        foundAgain.SetAsProcessed(ourMessage);
        await sut.ReleaseAsync(foundAgain);

        var stored = await sut.FindAsync(descriptor, ourMessage, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(3, stored.ProcessedMessages.Count);
    }

    [Fact]
    public async Task LockAsync_should_store_the_state_of_a_new_saga()
    {
        var dbSuffix = Guid.NewGuid().ToString();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var messageContext = CreateMessageContext<FakeMessage>();
        var correlationId = messageContext.CorrelationId;
        var state = DummyState.New();
        var sut = CreateSut(_fixture.CreateDbContext(dbSuffix));
        Assert.Null(await sut.FindAsync(descriptor, messageContext, CancellationToken.None));

        var saga = new SagaInstance<DummyState>(Guid.NewGuid().ToString(), messageContext.MessageId, correlationId, descriptor, state);
        await saga.LockAsync(sut, CancellationToken.None);

        // someone else finds it while its first message is still being handled
        var found = await CreateSut(_fixture.CreateDbContext(dbSuffix)).FindAsync(descriptor, messageContext, CancellationToken.None);
        var withState = Assert.IsAssignableFrom<ISagaInstance<DummyState>>(found);
        Assert.Equal(state, withState.State);
    }

    [Fact]
    public async Task FindAsync_should_return_a_new_state_for_a_saga_stored_without_one()
    {
        var db = _fixture.CreateDbContext();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, DummyState>();
        var messageContext = CreateMessageContext<FakeMessage>();
        var correlationId = messageContext.CorrelationId;

        // what a version that stored the state with the first release leaves behind while the first message is handled
        await db.SagaStates.InsertOneAsync(new Entities.SagaState
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId(),
            InstanceId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            TriggerMessageId = messageContext.MessageId,
            SagaType = descriptor.SagaType.FullName!,
            SagaStateType = descriptor.SagaStateType!.FullName,
            LockId = Guid.NewGuid().ToString(),
            LockTime = DateTimeOffset.UtcNow
        });

        var found = await CreateSut(db).FindAsync(descriptor, messageContext, CancellationToken.None);

        var withState = Assert.IsAssignableFrom<ISagaInstance<DummyState>>(found);
        Assert.Equal(new DummyState(), withState.State);
    }

    [Fact]
    public async Task Lock_and_release_should_work_on_a_database_with_unacknowledged_writes()
    {
        var database = new MongoClient(_fixture.ConnectionString).GetDatabase(
            _fixture.CreateDatabaseName(),
            new MongoDatabaseSettings { WriteConcern = WriteConcern.Unacknowledged });
        var sut = CreateSut(new DbContext(database));
        var saga = CreateSagaContext(CreateMessageContext<FakeMessage>());

        await saga.LockAsync(sut, CancellationToken.None);
        await sut.ReleaseAsync(saga);
    }

    [Fact]
    public async Task ReleaseAsync_should_not_write_when_it_is_cancelled()
    {
        var sut = CreateSut(_fixture.CreateDbContext());
        var messageContext = CreateMessageContext<FakeMessage>();
        var saga = CreateSagaContext(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sut.ReleaseAsync(saga, cancelled.Token));

        await Assert.ThrowsAsync<LockException>(async () => await CreateSagaContext(messageContext, saga).LockAsync(sut, CancellationToken.None));
    }

    private static ISagaInstance CreateSagaContext<TM>(IMessageContext<TM> messageContext, ISagaInstance sameAs)
        where TM : IMessage
        => new SagaInstance(sameAs.InstanceId, sameAs.TriggerMessageId, sameAs.CorrelationId, sameAs.Descriptor);

    [Fact]
    public async Task ReleaseLockAsync_should_release_lock_and_update_state()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        await sagaContext.LockAsync(sut, CancellationToken.None);

        sagaContext.SetAsProcessed(messageContext);

        var messageContext2 = CreateMessageContext<FakeMessage>();
        sagaContext.SetAsProcessed(messageContext2);

        sagaContext.MarkAsCompleted();

        await sut.ReleaseAsync(sagaContext);

        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var filter = filterBuilder.Eq(e => e.InstanceId, sagaContext.InstanceId);
        var unLockedState = await db.SagaStates.FindOneAsync(filter);
        Assert.NotNull(unLockedState);
        Assert.Null(unLockedState.LockTime);
        Assert.True(unLockedState.IsCompleted);
        Assert.NotNull(unLockedState.ProcessedMessages);
        Assert.NotEmpty(unLockedState.ProcessedMessages);
        Assert.Equal(2, unLockedState.ProcessedMessages.Count);
        Assert.Contains(unLockedState.ProcessedMessages, m => m.MessageId == messageContext.MessageId);
        Assert.Contains(unLockedState.ProcessedMessages, m => m.MessageId == messageContext2.MessageId);
    }
}
