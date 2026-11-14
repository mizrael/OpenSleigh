using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Integration;

/// <summary>
/// what is stored, and what is not, when a message handler fails.
/// </summary>
[Category("Integration")]
[Trait("Category", "Integration")]
public abstract class SqlSagaFailureTests
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

    protected SqlSagaFailureTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record Database(SagaDbContext Repository, SagaDbContext Verification);

    private Database CreateDatabase()
    {
        var dbName = Guid.NewGuid().ToString();
        var (repositoryDb, _) = _fixture.CreateDbContext(dbName);
        var (verificationDb, _) = _fixture.CreateDbContext(dbName);
        return new Database(repositoryDb, verificationDb);
    }

    private static SqlSagaStateRepository CreateRepository(SagaDbContext db, SqlSagaStateRepositoryOptions? options = null)
        => new(db, options ?? SqlSagaStateRepositoryOptions.Default, Serializer);

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
    private static async Task<ISagaInstance> StoreSagaAsync(SqlSagaStateRepository repository, IMessageContext<FakeMessage> firstMessage)
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
    private static async Task<ISagaInstance<CounterState>> FailHandlerAsync(SqlSagaStateRepository repository, IMessageContext<FakeMessage> message)
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

    private static async Task<(Entities.SagaState Saga, CounterState State)> ReadStoredSagaAsync(SagaDbContext db, string instanceId)
    {
        var saga = await db.SagaStates
            .Include(e => e.ProcessedMessages)
            .AsNoTracking()
            .SingleAsync(e => e.InstanceId == instanceId);

        return (saga, (CounterState)Serializer.Deserialize(saga.StateData, typeof(CounterState))!);
    }

    [Fact]
    public async Task AbandonAsync_should_release_the_saga_as_it_was_stored_and_record_the_failure()
    {
        var database = CreateDatabase();
        var repository = CreateRepository(database.Repository);
        var firstMessage = CreateMessage();
        var stored = await StoreSagaAsync(repository, firstMessage);

        var failedMessage = CreateMessage(firstMessage.CorrelationId);
        var failed = await FailHandlerAsync(repository, failedMessage);
        failed.ClearOutbox();
        var failure = CreateFailure(failed, failedMessage, FailureDecision.DeadLettered);
        failed.RecordFailure(failure);

        await repository.AbandonAsync(failed);

        var (saga, state) = await ReadStoredSagaAsync(database.Verification, stored.InstanceId);
        Assert.Equal(1, state.Value);
        Assert.False(saga.IsCompleted);
        Assert.Null(saga.LockTime);
        Assert.Equal(firstMessage.MessageId, Assert.Single(saga.ProcessedMessages).MessageId);
        Assert.Empty(await database.Verification.OutboxMessages.AsNoTracking().ToListAsync());

        var record = Assert.Single(await database.Verification.SagaMessageFailures.AsNoTracking().ToListAsync());
        Assert.Equal(failure.Id, record.Id);
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
        Assert.InRange(record.OccurredAt, failure.OccurredAt - StoredTimePrecision, failure.OccurredAt + StoredTimePrecision);
    }

    [Fact]
    public async Task AbandonAsync_should_let_the_message_be_handled_again_on_the_saga_as_it_was_stored()
    {
        var database = CreateDatabase();
        var repository = CreateRepository(database.Repository);
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

        var (saga, state) = await ReadStoredSagaAsync(database.Verification, stored.InstanceId);
        Assert.Equal(2, state.Value);
        Assert.Equal(2, saga.ProcessedMessages.Count);
        Assert.Empty(await database.Verification.SagaMessageFailures.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AbandonAsync_should_keep_the_row_of_a_saga_whose_first_message_failed_with_a_new_state()
    {
        var database = CreateDatabase();
        var repository = CreateRepository(database.Repository);
        var firstMessage = CreateMessage();
        var saga = NewSaga(firstMessage);
        await saga.LockAsync(repository, CancellationToken.None);
        saga.State.Value = 100;
        saga.RecordFailure(CreateFailure(saga, firstMessage, FailureDecision.DeadLettered));

        await repository.AbandonAsync(saga);

        var (row, state) = await ReadStoredSagaAsync(database.Verification, saga.InstanceId);
        Assert.Equal(0, state.Value);
        Assert.Null(row.LockTime);
        Assert.Empty(row.ProcessedMessages);
        Assert.Equal(saga.InstanceId, Assert.Single(await database.Verification.SagaMessageFailures.AsNoTracking().ToListAsync()).InstanceId);
    }

    [Fact]
    public async Task AbandonAsync_should_throw_and_record_nothing_when_the_lock_was_taken_over()
    {
        var database = CreateDatabase();
        var expiredLocks = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var repository = CreateRepository(database.Repository, expiredLocks);
        var firstMessage = CreateMessage();
        var stored = await StoreSagaAsync(repository, firstMessage);
        var failedMessage = CreateMessage(firstMessage.CorrelationId);
        var failed = await FailHandlerAsync(repository, failedMessage);
        failed.RecordFailure(CreateFailure(failed, failedMessage, FailureDecision.DeadLettered));

        var newOwnerRepository = CreateRepository(database.Verification, expiredLocks);
        var newOwner = (await newOwnerRepository.FindAsync(Descriptor, failedMessage))!;
        await newOwner.LockAsync(newOwnerRepository, CancellationToken.None);

        await Assert.ThrowsAsync<LockException>(async () => await repository.AbandonAsync(failed));

        database.Verification.ChangeTracker.Clear();
        var (saga, _) = await ReadStoredSagaAsync(database.Verification, stored.InstanceId);
        Assert.Equal(newOwner.LockId, saga.LockId);
        Assert.Empty(await database.Verification.SagaMessageFailures.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_compensation_should_run_on_the_saga_as_it_was_stored_and_be_stored_with_its_messages_and_the_failure()
    {
        var database = CreateDatabase();
        var repository = CreateRepository(database.Repository);
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

        var (saga, state) = await ReadStoredSagaAsync(database.Verification, stored.InstanceId);
        Assert.Equal(-1, state.Value);
        Assert.False(saga.IsCompleted);
        Assert.Null(saga.LockTime);
        Assert.Equal(
            new[] { firstMessage.MessageId, failedMessage.MessageId }.OrderBy(id => id),
            saga.ProcessedMessages.Select(m => m.MessageId).OrderBy(id => id));
        Assert.Equal(
            new[] { published.MessageId },
            await database.Verification.OutboxMessages.AsNoTracking().Select(e => e.MessageId).ToListAsync());
        var record = Assert.Single(await database.Verification.SagaMessageFailures.AsNoTracking().ToListAsync());
        Assert.Equal(FailureDecision.Compensated, record.Decision);
        Assert.Equal(failedMessage.MessageId, record.MessageId);
    }

    [Fact]
    public async Task A_failed_compensation_should_leave_the_saga_as_it_was_stored_and_record_why()
    {
        var database = CreateDatabase();
        var repository = CreateRepository(database.Repository);
        var firstMessage = CreateMessage();
        var stored = await StoreSagaAsync(repository, firstMessage);
        var failedMessage = CreateMessage(firstMessage.CorrelationId);
        var failed = await FailHandlerAsync(repository, failedMessage);

        var asStored = (ISagaInstance<CounterState>)(await repository.FindAsync(Descriptor, failedMessage))!;
        failed.HandLockTo(asStored);
        asStored.State.Value = -1;
        asStored.RecordFailure(CreateFailure(
            asStored, failedMessage, FailureDecision.CompensationFailed, new TimeoutException("the warehouse did not answer")));
        await repository.AbandonAsync(asStored);

        var (saga, state) = await ReadStoredSagaAsync(database.Verification, stored.InstanceId);
        Assert.Equal(1, state.Value);
        Assert.Null(saga.LockTime);
        Assert.Single(saga.ProcessedMessages);
        var record = Assert.Single(await database.Verification.SagaMessageFailures.AsNoTracking().ToListAsync());
        Assert.Equal(FailureDecision.CompensationFailed, record.Decision);
        Assert.Contains("the warehouse did not answer", record.CompensationError);
    }

    private static async Task<SagaMessageFailure[]> StoreFailuresAsync(SqlSagaStateRepository repository, params (FailureDecision Decision, TimeSpan Age)[] failures)
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
        var database = CreateDatabase();
        var repository = CreateRepository(database.Repository);
        var ofFirstSaga = await StoreFailuresAsync(repository,
            (FailureDecision.DeadLettered, TimeSpan.FromMinutes(30)),
            (FailureDecision.Compensated, TimeSpan.FromMinutes(20)),
            (FailureDecision.DeadLettered, TimeSpan.FromMinutes(10)));
        var ofSecondSaga = await StoreFailuresAsync(repository, (FailureDecision.CompensationFailed, TimeSpan.FromMinutes(5)));
        var sut = new SqlSagaFailureQuery(database.Verification);

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
        var database = CreateDatabase();
        var repository = CreateRepository(database.Repository);
        var failures = await StoreFailuresAsync(repository,
            (FailureDecision.DeadLettered, TimeSpan.FromDays(10)),
            (FailureDecision.DeadLettered, TimeSpan.FromDays(8)),
            (FailureDecision.DeadLettered, TimeSpan.FromDays(1)));
        var sut = new SqlSagaFailureQuery(database.Verification);

        // a local time: what is compared is the moment, whatever the offset it is given with
        var olderThan = (DateTimeOffset.UtcNow - TimeSpan.FromDays(7)).ToOffset(TimeSpan.FromHours(5));
        var removed = await sut.PurgeAsync(olderThan);

        Assert.Equal(2, removed);
        Assert.Equal(failures[2].Id, Assert.Single((await sut.GetAllAsync()).Items).Id);
    }
}
