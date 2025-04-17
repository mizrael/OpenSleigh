using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Utils;
using System.ComponentModel;

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
    public async Task LockAsync_should_throw_if_message_not_found()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await Assert.ThrowsAsync<ArgumentException>(async () => await sut.LockAsync(message));
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
        appendedMessage.Should().NotBeNull();
        appendedMessage.LockId.Should().BeNull();
        appendedMessage.LockTime.Should().BeNull();
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
    public async Task ReadMessagesToProcess_should_return_available_messages()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var messages = await sut.ReadPendingAsync();
        messages.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LockAsync_should_lock_existing_message()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var lockId = await sut.LockAsync(message);

        var lockedMessage = await db.OutboxMessages.FirstOrDefaultAsync(e => e.MessageId == message.MessageId);
        lockedMessage.Should().NotBeNull();
        lockedMessage.LockId.Should().Be(lockId);
        lockedMessage.LockTime.Should().NotBeNull();
    }

    [Fact]
    public async Task LockAsync_should_throw_if_message_already_locked()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        await sut.LockAsync(message);

        await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(message));
    }

    [Fact]
    public async Task LockAsync_should_throw_if_message_not_existing()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await Assert.ThrowsAsync<ArgumentException>(async () => await sut.LockAsync(message));
    }

    [Fact]
    public async Task DeleteAsync_should_throw_if_message_not_found()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await sut.DeleteAsync(message, "lorem"));
        ex.Message.Should().Contain($"message '{message.MessageId}' not found");
    }

    [Fact]
    public async Task DeleteAsync_should_throw_if_message_not_locked()
    {
        var message = CreateMessage();
        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([message]);

        var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.DeleteAsync(message, "lorem"));
        ex.Message.Should().Contain($"message '{message.MessageId}' is not locked");
    }

    [Fact]
    public async Task DeleteAsync_should_throw_if_lock_invalid()
    {
        var message = CreateMessage();
        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([message]);
        await sut.LockAsync(message);

        var lockId = Guid.NewGuid().ToString();

        var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.DeleteAsync(message, lockId));
        ex.Message.Should().Contain($"invalid lock id '{lockId}' on message '{message.MessageId}'");
    }

    [Fact]
    public async Task DeleteAsync_should_delete_message()
    {
        var message = CreateMessage();

        var (db,_) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([message]);
        var lockId = await sut.LockAsync(message);
        await sut.DeleteAsync(message, lockId);

        var lockedMessage = await db.OutboxMessages.FirstOrDefaultAsync(e => e.MessageId == message.MessageId);
        lockedMessage.Should().BeNull();
    }

    protected SqlOutboxRepository CreateSut(SagaDbContext db, DuplicateKeyDetector duplicateKeyDetector)
    {
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeMessage));

        var sut = new SqlOutboxRepository(db, typeResolver, SqlOutboxRepositoryOptions.Default, new JsonSerializer(), duplicateKeyDetector);
        return sut;
    }
}
