using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Utils;
using System.ComponentModel;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public abstract class SqlOutboxRepositoryTests
{
    private readonly DbFixture _fixture;

    public SqlOutboxRepositoryTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    protected abstract SqlOutboxRepository CreateSut(SagaDbContext db);

    private static MessageEnvelope CreateMessage(TimeSpan delay = default)
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");
        sysInfo.Id.Returns("sender");
        return MessageEnvelope.Create(new FakeMessage(), sysInfo, delay);
    }

    private static MessageEnvelope CreateMessageDueAt(DateTimeOffset dueTime)
    {
        var serializer = new JsonSerializer();
        Assert.True(MessageEnvelope.TryCreate(
            serializer.Serialize(new FakeMessage()),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            typeof(FakeMessage),
            "sender",
            dueTime,
            serializer,
            out var message));
        return message;
    }

    [Fact]
    public async Task ReadPendingAsync_should_not_return_a_message_before_it_is_due()
    {
        var immediate = CreateMessage();
        var delayed = CreateMessage(TimeSpan.FromHours(1));

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([delayed, immediate]);

        var messages = await sut.ReadPendingAsync();

        Assert.Equal(immediate.MessageId, Assert.Single(messages).MessageId);
        Assert.Equal(2, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_a_delayed_message_once_it_is_due_with_its_due_time()
    {
        var dueTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        var due = CreateMessageDueAt(dueTime);
        var immediate = CreateMessage();

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([due, immediate]);

        var messages = (await sut.ReadPendingAsync()).ToDictionary(m => m.MessageId);

        Assert.Equal(2, messages.Count);
        Assert.Null(messages[immediate.MessageId].DeliverAt);

        // a database may keep less of a time than .NET does
        var readDueTime = Assert.NotNull(messages[due.MessageId].DeliverAt);
        Assert.Equal(dueTime.UtcDateTime, readDueTime.UtcDateTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_a_message_when_its_delay_has_passed()
    {
        var delay = TimeSpan.FromSeconds(1);
        var delayed = CreateMessage(delay);

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([delayed]);

        Assert.Empty(await sut.ReadPendingAsync());

        await Task.Delay(delay + delay);

        Assert.Equal(delayed.MessageId, Assert.Single(await sut.ReadPendingAsync()).MessageId);
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_the_due_messages_when_more_than_a_batch_of_others_is_waiting()
    {
        var batchSize = SqlOutboxRepositoryOptions.Default.MaxMessagesToPull;
        var notDue = Enumerable.Range(0, 2 * batchSize).Select(_ => CreateMessage(TimeSpan.FromHours(1))).ToArray();
        var due = new[] { CreateMessage(), CreateMessageDueAt(DateTimeOffset.UtcNow.AddSeconds(-1)), CreateMessage() };

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync(notDue);
        await sut.AppendAsync(due);

        var messages = await sut.ReadPendingAsync();

        Assert.Equal(due.Select(m => m.MessageId).OrderBy(id => id), messages.Select(m => m.MessageId).OrderBy(id => id));
    }

    [Fact]
    public async Task AppendAsync_should_store_a_message_without_a_delay_as_due_immediately()
    {
        var message = CreateMessage();

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var stored = await db.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(Entities.OutboxMessage.DueImmediately, stored.DeliverAt);
    }

    [Fact]
    public async Task AppendAsync_should_append_message()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var result = await sut.AppendAsync([message]);
        Assert.Equal(OutboxAppendResult.Success, result);

        var appendedMessage = await db.OutboxMessages.FirstOrDefaultAsync(e => e.MessageId == message.MessageId);
        Assert.NotNull(appendedMessage);
    }

    [Fact]
    public async Task AppendAsync_should_fail_if_message_already_appended_same_context()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var result = await sut.AppendAsync([message]);
        Assert.Equal(OutboxAppendResult.Duplicate, result);
    }

    [Fact]
    public async Task AppendAsync_should_fail_if_message_already_appended_on_different_db_context()
    {
        var message = CreateMessage();

        var dbName = Guid.NewGuid().ToString();
        var (db, _) = _fixture.CreateDbContext(dbName);
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var (db2, _) = _fixture.CreateDbContext(dbName);
        var sut2 = CreateSut(db2);
        var result = await sut2.AppendAsync([message]);
        Assert.Equal(OutboxAppendResult.Duplicate, result);
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_available_messages()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var messages = await sut.ReadPendingAsync();
        Assert.NotNull(messages);
        Assert.Single(messages);
        Assert.Equivalent(message.MessageId, messages.First().MessageId);
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_only_unlocked_messages()
    {
        var serializer = new JsonSerializer();
        var messages = Enumerable.Range(1, 12)
            .Select(i => CreateMessage())
            .Select(m => Entities.OutboxMessage.Map(m, serializer))
            .ToArray();

        var dbName = "commitOutboxTest";

        var (seedCtx, _) = _fixture.CreateDbContext(dbName);
        seedCtx.OutboxMessages.AddRange(messages);
        await seedCtx.SaveChangesAsync();

        // messages are locked inside the transaction
        var (lockCtx, _) = _fixture.CreateDbContext(dbName);

        // opening a transaction is mandatory to ensure the lock on the messages is acquired
        // otherwise other queries would pull the same messages
        await using var transaction = await lockCtx.BeginTransactionAsync();

        var lockSut = CreateSut(lockCtx);
        var lockedMessages = await lockSut.ReadPendingAsync();
        Assert.NotNull(lockedMessages);
        Assert.Equal(10, lockedMessages.Count());

        // this should not return any of the previous results as those messages are still locked inside the first transaction
        var (secondCtx, _) = _fixture.CreateDbContext(dbName);
        await using var tr2 = await secondCtx.BeginTransactionAsync();
        var secondSut = CreateSut(secondCtx);
        var secondBatch = await secondSut.ReadPendingAsync();
        Assert.NotNull(secondBatch);
        Assert.Equal(2, secondBatch.Count());

        foreach (var lockedMsg in lockedMessages)
            Assert.DoesNotContain(lockedMsg, secondBatch);

        await lockCtx.SaveChangesAsync();

        await transaction.CommitAsync();
        await tr2.CommitAsync();

        // now that the previous transactions are done, we should be able to fetch the remaining messages
        var (pendingMsgsCtx, _) = _fixture.CreateDbContext(dbName);
        await using var tr3 = await pendingMsgsCtx.BeginTransactionAsync();
        var pendingMsgsSut = CreateSut(pendingMsgsCtx);
        var remainingMessages = await pendingMsgsSut.ReadPendingAsync();
        Assert.NotNull(remainingMessages);
        Assert.Equal(10, remainingMessages.Count());
        await tr3.CommitAsync();
    }

    [Fact]
    public async Task AppendAsync_should_keep_working_after_a_duplicate_on_the_same_repository()
    {
        var message = CreateMessage();
        var anotherMessage = CreateMessage();

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        Assert.Equal(OutboxAppendResult.Success, await sut.AppendAsync([message]));
        Assert.Equal(OutboxAppendResult.Duplicate, await sut.AppendAsync([message]));
        Assert.Equal(OutboxAppendResult.Duplicate, await sut.AppendAsync([message]));
        Assert.Equal(OutboxAppendResult.Success, await sut.AppendAsync([anotherMessage]));

        var storedIds = await db.OutboxMessages.AsNoTracking().Select(e => e.MessageId).ToListAsync();
        Assert.Equal(2, storedIds.Count);
        Assert.Contains(message.MessageId, storedIds);
        Assert.Contains(anotherMessage.MessageId, storedIds);
    }

    [Fact]
    public async Task AppendAsync_should_not_keep_appended_messages_tracked()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([CreateMessage(), CreateMessage()]);
        var duplicate = CreateMessage();
        await sut.AppendAsync([duplicate]);
        await sut.AppendAsync([duplicate]);

        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task DeleteAsync_should_delete_message()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([message]);
        await sut.DeleteAsync(message);

        var lockedMessage = await db.OutboxMessages.FirstOrDefaultAsync(e => e.MessageId == message.MessageId);
        Assert.Null(lockedMessage);
    }

    [Fact]
    public async Task DeleteAsync_should_delete_a_batch_of_messages_and_leave_the_others()
    {
        var deleted = new[] { CreateMessage(), CreateMessage() };
        var kept = CreateMessage();

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([.. deleted, kept]);
        await sut.DeleteAsync(deleted);

        var remaining = await db.OutboxMessages.AsNoTracking().Select(e => e.MessageId).ToListAsync();
        Assert.Equal(new[] { kept.MessageId }, remaining);
    }

    [Fact]
    public async Task DeleteAsync_should_delete_the_rest_of_a_batch_when_one_of_its_messages_is_already_gone()
    {
        var stored = CreateMessage();
        var alreadyGone = CreateMessage();

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([stored]);
        await sut.DeleteAsync(new[] { stored, alreadyGone });

        Assert.False(await db.OutboxMessages.AsNoTracking().AnyAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task DeleteAsync_should_delete_a_batch_that_lists_a_message_twice()
    {
        var message = CreateMessage();
        var kept = CreateMessage();

        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([message, kept]);
        await sut.DeleteAsync(new[] { message, message });

        var remaining = await db.OutboxMessages.AsNoTracking().Select(e => e.MessageId).ToListAsync();
        Assert.Equal(new[] { kept.MessageId }, remaining);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
