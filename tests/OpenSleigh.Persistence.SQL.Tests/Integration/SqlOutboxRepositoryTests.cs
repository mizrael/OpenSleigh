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

    private static MessageEnvelope CreateMessage()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");
        sysInfo.Id.Returns("sender");
        return MessageEnvelope.Create(new FakeMessage(), sysInfo);
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
}
