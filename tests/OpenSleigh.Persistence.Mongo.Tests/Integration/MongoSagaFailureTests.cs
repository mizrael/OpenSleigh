using MongoDB.Bson;
using MongoDB.Driver;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration;

/// <summary>
/// what is stored, and what is not, when a message handler fails.
/// </summary>
[Category("Integration")]
[Trait("Category", "Integration")]
public class MongoSagaFailureTests : IClassFixture<DbFixture>
{
    public class CounterState
    {
        public int Value { get; set; }
    }

    public class CounterSaga : ISaga<CounterState>, IStartedBy<FakeMessage>
    {
        public CounterSaga(ISagaInstance<CounterState> context)
        {
            Context = context;
        }

        public ISagaInstance<CounterState> Context { get; }

        ISagaInstance ISaga.Context => Context;

        public ValueTask HandleAsync(IMessageContext<FakeMessage> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private static readonly SagaDescriptor Descriptor = SagaDescriptor.Create<CounterSaga, CounterState>();
    private static readonly JsonSerializer Serializer = new();
    private static readonly TimeSpan StoredTimePrecision = TimeSpan.FromMilliseconds(1);

    private readonly DbFixture _fixture;

    public MongoSagaFailureTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    private static MongoSagaStateRepository CreateRepository(IDbContext db, MongoSagaStateRepositoryOptions? options = null)
        => new(db, options, Serializer);

    private static IMessageContext<FakeMessage> CreateMessage(string? correlationId = null)
    {
        var messageContext = Substitute.For<IMessageContext<FakeMessage>>();
        messageContext.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext.CorrelationId.Returns(correlationId ?? Guid.NewGuid().ToString());
        messageContext.Message.Returns(new FakeMessage());
        return messageContext;
    }

    private static SagaInstance<CounterState> NewSaga(IMessageContext<FakeMessage> message)
        => new(Guid.NewGuid().ToString(), message.MessageId, message.CorrelationId, Descriptor, new CounterState());

    /// <summary>
    /// a saga that handled its first message, which set its counter to 1.
    /// </summary>
    private static async Task<ISagaInstance> StoreSagaAsync(MongoSagaStateRepository repository, IMessageContext<FakeMessage> firstMessage)
    {
        var saga = NewSaga(firstMessage);
        await saga.LockAsync(repository, CancellationToken.None);
        saga.State.Value = 1;
        saga.SetAsProcessed(firstMessage);
        await repository.ReleaseAsync(saga);
        return saga;
    }

    /// <summary>
    /// finds and locks the saga for a message, and does what a handler that fails half way does to the instance.
    /// </summary>
    private static async Task<ISagaInstance<CounterState>> FailHandlerAsync(MongoSagaStateRepository repository, IMessageContext<FakeMessage> message)
    {
        var saga = (ISagaInstance<CounterState>)(await repository.FindAsync(Descriptor, message))!;
        await saga.LockAsync(repository, CancellationToken.None);

        saga.State.Value = 100;
        saga.MarkAsCompleted();
        saga.Publish(OpenSleigh.Outbox.MessageEnvelope.Create(new FakeMessage(), saga));
        return saga;
    }

    private static SagaMessageFailure CreateFailure(
        ISagaInstance saga,
        IMessageContext<FakeMessage> message,
        FailureDecision decision,
        Exception? compensationError = null)
        => SagaMessageFailure.Create(saga, message, new InvalidOperationException("out of stock"), attempt: 1, decision, compensationError);

    private static async Task<(Entities.SagaState Saga, CounterState State)> ReadStoredSagaAsync(IDbContext db, string instanceId)
    {
        var saga = await db.SagaStates
            .Find(Builders<Entities.SagaState>.Filter.Eq(e => e.InstanceId, instanceId))
            .SingleAsync();

        return (saga, (CounterState)Serializer.Deserialize(saga.StateData, typeof(CounterState))!);
    }

    private static Task<List<Entities.SagaFailureRecord>> ReadFailureRecordsAsync(IDbContext db)
        => db.SagaMessageFailures.Find(FilterDefinition<Entities.SagaFailureRecord>.Empty).ToListAsync();

    private static Task<long> CountOutboxAsync(IDbContext db)
        => db.OutboxMessages.CountDocumentsAsync(FilterDefinition<Entities.OutboxMessage>.Empty);

    [Fact]
    public async Task AbandonAsync_should_release_the_saga_as_it_was_stored_and_record_the_failure()
    {
        var db = _fixture.CreateDbContext();
        var repository = CreateRepository(db);
        var firstMessage = CreateMessage();
        var stored = await StoreSagaAsync(repository, firstMessage);

        var failedMessage = CreateMessage(firstMessage.CorrelationId);
        var failed = await FailHandlerAsync(repository, failedMessage);
        var failure = CreateFailure(failed, failedMessage, FailureDecision.DeadLettered);
        failed.RecordFailure(failure);

        await repository.AbandonAsync(failed);

        var (saga, state) = await ReadStoredSagaAsync(db, stored.InstanceId);
        Assert.Equal(1, state.Value);
        Assert.False(saga.IsCompleted);
        Assert.Null(saga.LockTime);
        Assert.Equal(firstMessage.MessageId, Assert.Single(saga.ProcessedMessages).MessageId);
        Assert.Equal(0, await CountOutboxAsync(db));

        var record = Assert.Single(await ReadFailureRecordsAsync(db));
        Assert.Equal(failure.Id, record.FailureId);
        Assert.Equal(stored.InstanceId, record.InstanceId);
        Assert.Equal(firstMessage.CorrelationId, record.CorrelationId);
        Assert.Equal(typeof(CounterSaga).FullName, record.SagaType);
        Assert.Equal(failedMessage.MessageId, record.MessageId);
        Assert.Equal(typeof(FakeMessage).FullName, record.MessageType);
        Assert.Equal(1, record.Attempt);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("out of stock", record.ExceptionMessage);
        Assert.Equal(FailureDecision.DeadLettered, record.Decision);
        Assert.Null(record.RetryAt);
        Assert.Null(record.CompensationError);

        var read = record.ToFailure();
        Assert.Equal(TimeSpan.Zero, read.OccurredAt.Offset);
        Assert.InRange(read.OccurredAt, failure.OccurredAt - StoredTimePrecision, failure.OccurredAt + StoredTimePrecision);
    }

    [Fact]
    public async Task A_failure_should_be_stored_with_a_date_and_the_name_of_the_decision()
    {
        var db = _fixture.CreateDbContext();
        var repository = CreateRepository(db);
        var firstMessage = CreateMessage();
        var saga = NewSaga(firstMessage);
        await saga.LockAsync(repository, CancellationToken.None);
        saga.RecordFailure(CreateFailure(saga, firstMessage, FailureDecision.DeadLettered));
        await repository.AbandonAsync(saga);

        var document = await db.SagaMessageFailures.Database
            .GetCollection<BsonDocument>(db.SagaMessageFailures.CollectionNamespace.CollectionName)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .SingleAsync();

        // sorted, purged and expired by the server, and read by people
        Assert.Equal(BsonType.DateTime, document[nameof(Entities.SagaFailureRecord.OccurredAt)].BsonType);
        Assert.Equal(nameof(FailureDecision.DeadLettered), document[nameof(Entities.SagaFailureRecord.Decision)].AsString);
        Assert.False(document.Contains(nameof(Entities.SagaFailureRecord.RetryAt)));
    }

    [Fact]
    public async Task AbandonAsync_should_let_the_message_be_handled_again_on_the_saga_as_it_was_stored()
    {
        var db = _fixture.CreateDbContext();
        var repository = CreateRepository(db);
        var firstMessage = CreateMessage();
        var stored = await StoreSagaAsync(repository, firstMessage);
        var failedMessage = CreateMessage(firstMessage.CorrelationId);
        await repository.AbandonAsync(await FailHandlerAsync(repository, failedMessage));

        var again = (ISagaInstance<CounterState>)(await repository.FindAsync(Descriptor, failedMessage))!;
        Assert.Equal(1, again.State.Value);
        Assert.False(again.IsCompleted);
        Assert.True(again.CanProcess(failedMessage));
        await again.LockAsync(repository, CancellationToken.None);
        again.State.Value = 2;
        again.SetAsProcessed(failedMessage);
        await repository.CommitAsync(again);

        var (saga, state) = await ReadStoredSagaAsync(db, stored.InstanceId);
        Assert.Equal(2, state.Value);
        Assert.Equal(2, saga.ProcessedMessages.Count);
        Assert.Empty(await ReadFailureRecordsAsync(db));
    }

    [Fact]
    public async Task AbandonAsync_should_keep_the_document_of_a_saga_whose_first_message_failed_with_a_new_state()
    {
        var db = _fixture.CreateDbContext();
        var repository = CreateRepository(db);
        var firstMessage = CreateMessage();
        var saga = NewSaga(firstMessage);
        await saga.LockAsync(repository, CancellationToken.None);
        saga.State.Value = 100;
        saga.RecordFailure(CreateFailure(saga, firstMessage, FailureDecision.DeadLettered));

        await repository.AbandonAsync(saga);

        var (document, state) = await ReadStoredSagaAsync(db, saga.InstanceId);
        Assert.Equal(0, state.Value);
        Assert.Null(document.LockTime);
        Assert.Empty(document.ProcessedMessages);
        Assert.Equal(saga.InstanceId, Assert.Single(await ReadFailureRecordsAsync(db)).InstanceId);
    }

    [Fact]
    public async Task AbandonAsync_should_throw_and_leave_no_record_when_the_lock_was_taken_over()
    {
        var db = _fixture.CreateDbContext();
        var expiredLocks = new MongoSagaStateRepositoryOptions(TimeSpan.Zero);
        var repository = CreateRepository(db, expiredLocks);
        var firstMessage = CreateMessage();
        var stored = await StoreSagaAsync(repository, firstMessage);
        var failedMessage = CreateMessage(firstMessage.CorrelationId);
        var failed = await FailHandlerAsync(repository, failedMessage);
        failed.RecordFailure(CreateFailure(failed, failedMessage, FailureDecision.DeadLettered));

        var newOwnerRepository = CreateRepository(db, expiredLocks);
        var newOwner = (await newOwnerRepository.FindAsync(Descriptor, failedMessage))!;
        await newOwner.LockAsync(newOwnerRepository, CancellationToken.None);

        await Assert.ThrowsAsync<LockException>(async () => await repository.AbandonAsync(failed));

        var (saga, _) = await ReadStoredSagaAsync(db, stored.InstanceId);
        Assert.Equal(newOwner.LockId, saga.LockId);
        Assert.Empty(await ReadFailureRecordsAsync(db));
    }

    [Fact]
    public async Task A_compensation_should_run_on_the_saga_as_it_was_stored_and_be_stored_with_its_messages_and_the_failure()
    {
        var db = _fixture.CreateDbContext();
        var repository = CreateRepository(db);
        var firstMessage = CreateMessage();
        var stored = await StoreSagaAsync(repository, firstMessage);
        var failedMessage = CreateMessage(firstMessage.CorrelationId);
        var failed = await FailHandlerAsync(repository, failedMessage);

        // what the saga pipeline does when the saga has a compensation handler for the message
        var asStored = (ISagaInstance<CounterState>)(await repository.FindAsync(Descriptor, failedMessage))!;
        failed.HandLockTo(asStored);
        Assert.Equal(1, asStored.State.Value);
        Assert.False(asStored.IsCompleted);

        asStored.State.Value = -1;
        var published = OpenSleigh.Outbox.MessageEnvelope.Create(new FakeMessage(), asStored);
        asStored.Publish(published);
        asStored.SetAsProcessed(failedMessage);
        asStored.RecordFailure(CreateFailure(asStored, failedMessage, FailureDecision.Compensated));
        await repository.CommitAsync(asStored);

        var (saga, state) = await ReadStoredSagaAsync(db, stored.InstanceId);
        Assert.Equal(-1, state.Value);
        Assert.False(saga.IsCompleted);
        Assert.Null(saga.LockTime);
        Assert.Equal(
            new[] { firstMessage.MessageId, failedMessage.MessageId }.OrderBy(id => id),
            saga.ProcessedMessages.Select(m => m.MessageId).OrderBy(id => id));

        var outbox = await db.OutboxMessages.Find(FilterDefinition<Entities.OutboxMessage>.Empty).ToListAsync();
        var outboxMessage = Assert.Single(outbox);
        Assert.Equal(published.MessageId, outboxMessage.MessageId);
        Assert.Null(outboxMessage.LockId);

        var record = Assert.Single(await ReadFailureRecordsAsync(db));
        Assert.Equal(FailureDecision.Compensated, record.Decision);
        Assert.Equal(failedMessage.MessageId, record.MessageId);
    }

    [Fact]
    public async Task A_compensation_that_cannot_be_stored_should_leave_neither_its_messages_nor_a_record()
    {
        var db = _fixture.CreateDbContext();
        var expiredLocks = new MongoSagaStateRepositoryOptions(TimeSpan.Zero);
        var repository = CreateRepository(db, expiredLocks);
        var firstMessage = CreateMessage();
        var stored = await StoreSagaAsync(repository, firstMessage);
        var failedMessage = CreateMessage(firstMessage.CorrelationId);
        var failed = await FailHandlerAsync(repository, failedMessage);
        var asStored = (ISagaInstance<CounterState>)(await repository.FindAsync(Descriptor, failedMessage))!;
        failed.HandLockTo(asStored);
        asStored.State.Value = -1;
        asStored.Publish(OpenSleigh.Outbox.MessageEnvelope.Create(new FakeMessage(), asStored));
        asStored.SetAsProcessed(failedMessage);
        asStored.RecordFailure(CreateFailure(asStored, failedMessage, FailureDecision.Compensated));

        var newOwnerRepository = CreateRepository(db, expiredLocks);
        var newOwner = (await newOwnerRepository.FindAsync(Descriptor, failedMessage))!;
        await newOwner.LockAsync(newOwnerRepository, CancellationToken.None);

        await Assert.ThrowsAsync<LockException>(async () => await repository.CommitAsync(asStored));

        var (_, state) = await ReadStoredSagaAsync(db, stored.InstanceId);
        Assert.Equal(1, state.Value);
        Assert.Equal(0, await CountOutboxAsync(db));
        Assert.Empty(await ReadFailureRecordsAsync(db));
    }

    private static async Task<SagaMessageFailure[]> StoreFailuresAsync(MongoSagaStateRepository repository, params (FailureDecision Decision, TimeSpan Age)[] failures)
    {
        var firstMessage = CreateMessage();
        await StoreSagaAsync(repository, firstMessage);
        var now = DateTimeOffset.UtcNow;
        var stored = new List<SagaMessageFailure>();

        foreach (var (decision, age) in failures)
        {
            var message = CreateMessage(firstMessage.CorrelationId);
            var saga = (await repository.FindAsync(Descriptor, message))!;
            await saga.LockAsync(repository, CancellationToken.None);
            var failure = CreateFailure(saga, message, decision) with { OccurredAt = now - age };
            saga.RecordFailure(failure);
            await repository.AbandonAsync(saga);
            stored.Add(failure);
        }

        return stored.ToArray();
    }

    [Fact]
    public async Task Failures_should_be_read_most_recent_first_filtered_and_paged()
    {
        var db = _fixture.CreateDbContext();
        var repository = CreateRepository(db);
        var ofFirstSaga = await StoreFailuresAsync(repository,
            (FailureDecision.DeadLettered, TimeSpan.FromMinutes(30)),
            (FailureDecision.Compensated, TimeSpan.FromMinutes(20)),
            (FailureDecision.DeadLettered, TimeSpan.FromMinutes(10)));
        var ofSecondSaga = await StoreFailuresAsync(repository, (FailureDecision.CompensationFailed, TimeSpan.FromMinutes(5)));
        var sut = new MongoSagaFailureQuery(db);

        var all = await sut.GetAllAsync();
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(
            new[] { ofSecondSaga[0].Id, ofFirstSaga[2].Id, ofFirstSaga[1].Id, ofFirstSaga[0].Id },
            all.Items.Select(f => f.Id));

        var byInstance = await sut.GetAllAsync(new SagaFailureQueryFilter { InstanceId = ofFirstSaga[0].InstanceId });
        Assert.Equal(3, byInstance.TotalCount);
        Assert.All(byInstance.Items, f => Assert.Equal(ofFirstSaga[0].InstanceId, f.InstanceId));

        var byCorrelation = await sut.GetAllAsync(new SagaFailureQueryFilter { CorrelationId = ofSecondSaga[0].CorrelationId });
        Assert.Equal(ofSecondSaga[0].Id, Assert.Single(byCorrelation.Items).Id);

        var byDecision = await sut.GetAllAsync(new SagaFailureQueryFilter { Decision = FailureDecision.DeadLettered });
        Assert.Equal(new[] { ofFirstSaga[2].Id, ofFirstSaga[0].Id }, byDecision.Items.Select(f => f.Id));

        var byTypes = await sut.GetAllAsync(new SagaFailureQueryFilter
        {
            SagaType = typeof(CounterSaga).FullName,
            MessageType = typeof(FakeMessage).FullName
        });
        Assert.Equal(4, byTypes.TotalCount);
        Assert.Empty((await sut.GetAllAsync(new SagaFailureQueryFilter { SagaType = "another saga" })).Items);

        var secondPage = await sut.GetAllAsync(new SagaFailureQueryFilter { Page = 2, PageSize = 3 });
        Assert.Equal(4, secondPage.TotalCount);
        Assert.Equal(ofFirstSaga[0].Id, Assert.Single(secondPage.Items).Id);
        Assert.Equal(FailureDecision.DeadLettered, secondPage.Items[0].Decision);
        Assert.Equal("out of stock", secondPage.Items[0].ExceptionMessage);
    }

    [Fact]
    public async Task PurgeAsync_should_remove_the_failures_that_occurred_before_the_given_time()
    {
        var db = _fixture.CreateDbContext();
        var repository = CreateRepository(db);
        var failures = await StoreFailuresAsync(repository,
            (FailureDecision.DeadLettered, TimeSpan.FromDays(10)),
            (FailureDecision.DeadLettered, TimeSpan.FromDays(8)),
            (FailureDecision.DeadLettered, TimeSpan.FromDays(1)));
        var sut = new MongoSagaFailureQuery(db);

        // a local time: what is compared is the moment, whatever the offset it is given with
        var olderThan = (DateTimeOffset.UtcNow - TimeSpan.FromDays(7)).ToOffset(TimeSpan.FromHours(5));
        var removed = await sut.PurgeAsync(olderThan);

        Assert.Equal(2, removed);
        Assert.Equal(failures[2].Id, Assert.Single((await sut.GetAllAsync()).Items).Id);
    }

    private static async Task<BsonDocument> ReadOccurredAtIndexAsync(IDbContext db)
    {
        var indexes = await (await db.SagaMessageFailures.Indexes.ListAsync()).ToListAsync();
        return indexes.Single(index => index["name"].AsString == DbContext.FailuresOccurredAtIndexName);
    }

    [Fact]
    public async Task Failure_records_should_only_expire_when_asked_to_and_the_expiry_should_follow_the_configuration()
    {
        var database = new MongoClient(_fixture.ConnectionString).GetDatabase(_fixture.CreateDatabaseName());

        var withoutExpiry = await ReadOccurredAtIndexAsync(new DbContext(database));
        Assert.False(withoutExpiry.Contains("expireAfterSeconds"));

        var withExpiry = await ReadOccurredAtIndexAsync(new DbContext(database, TimeSpan.FromDays(30)));
        Assert.Equal(TimeSpan.FromDays(30).TotalSeconds, withExpiry["expireAfterSeconds"].ToDouble());

        var withAnotherExpiry = await ReadOccurredAtIndexAsync(new DbContext(database, TimeSpan.FromDays(7)));
        Assert.Equal(TimeSpan.FromDays(7).TotalSeconds, withAnotherExpiry["expireAfterSeconds"].ToDouble());

        var withoutExpiryAgain = await ReadOccurredAtIndexAsync(new DbContext(database));
        Assert.False(withoutExpiryAgain.Contains("expireAfterSeconds"));
    }

    [Fact]
    public async Task Instances_that_start_together_with_a_new_expiry_should_all_start()
    {
        var database = new MongoClient(_fixture.ConnectionString).GetDatabase(_fixture.CreateDatabaseName());
        _ = new DbContext(database);

        var starting = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => new DbContext(database, TimeSpan.FromDays(30))))
            .ToArray();
        var contexts = await Task.WhenAll(starting);

        var index = await ReadOccurredAtIndexAsync(contexts[0]);
        Assert.Equal(TimeSpan.FromDays(30).TotalSeconds, index["expireAfterSeconds"].ToDouble());
    }
}
