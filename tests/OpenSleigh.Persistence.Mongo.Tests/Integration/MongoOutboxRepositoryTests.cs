using MongoDB.Driver;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using OpenSleigh.Transport;
using System.ComponentModel;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public class MongoOutboxRepositoryTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public MongoOutboxRepositoryTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    private static MessageEnvelope CreateMessage()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");
        sysInfo.Id.Returns("sender");
        return MessageEnvelope.Create(new FakeMessage(), sysInfo);
    }

    private MongoOutboxRepository CreateSut(IDbContext db)
    {
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeMessage));

        var sut = new MongoOutboxRepository(db, MongoOutboxRepositoryOptions.Default, typeResolver, new JsonSerializer());
        return sut;
    }

    [Fact]
    public async Task AppendAsync_should_append_message()
    {
        var message = CreateMessage();

        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var result = await sut.AppendAsync([message]);
        Assert.Equal(OutboxAppendResult.Success, result);

        var filter = Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, message.MessageId);

        var appendedMessage = await db.OutboxMessages.FindOneAsync(filter);
        Assert.NotNull(appendedMessage);
        Assert.Null(appendedMessage.LockId);
        Assert.Null(appendedMessage.LockTime);
    }

    [Fact]
    public async Task AppendAsync_should_fail_if_message_already_appended_same_context()
    {
        var message = CreateMessage();

        var db = _fixture.CreateDbContext();
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
        var db = _fixture.CreateDbContext(dbName);
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var db2 = _fixture.CreateDbContext(dbName);
        var sut2 = CreateSut(db2);
        var result = await sut2.AppendAsync([message]);
        Assert.Equal(OutboxAppendResult.Duplicate, result);
    }

    [Fact]
    public async Task DeleteAsync_should_throw_if_message_not_found()
    {
        var message = CreateMessage();

        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await sut.DeleteAsync(message));
        Assert.Contains($"message '{message.MessageId}' not found", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_should_delete_message()
    {
        var message = CreateMessage();

        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([message]);
        await sut.DeleteAsync(message);

        var filter = Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, message.MessageId);
        var lockedMessage = await db.OutboxMessages.FindOneAsync(filter);
        Assert.Null(lockedMessage);
    }

    [Fact]
    public async Task ReadMessagesToProcess_should_return_available_messages()
    {
        var message = CreateMessage();

        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var messages = await sut.ReadPendingAsync();
        Assert.NotNull(messages);
        Assert.NotEmpty(messages);
    }

    [Fact]
    public async Task ReadPendingAsync_should_not_return_messages_another_reader_has_claimed()
    {
        var db = _fixture.CreateDbContext();
        var firstRelay = CreateSut(db);
        var secondRelay = CreateSut(db);
        await firstRelay.AppendAsync([CreateMessage(), CreateMessage(), CreateMessage()]);

        var readByFirst = await firstRelay.ReadPendingAsync();
        var readBySecond = await secondRelay.ReadPendingAsync();

        Assert.Equal(3, System.Linq.Enumerable.Count(readByFirst));
        Assert.Empty(readBySecond);
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_messages_whose_claim_has_expired()
    {
        var db = _fixture.CreateDbContext();
        var message = CreateMessage();
        var options = new MongoOutboxRepositoryOptions(TimeSpan.Zero);
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeMessage));
        var sut = new MongoOutboxRepository(db, options, typeResolver, new JsonSerializer());
        await sut.AppendAsync([message]);

        await sut.ReadPendingAsync();
        await Task.Delay(50);
        var readAgain = await sut.ReadPendingAsync();

        Assert.Equal(message.MessageId, Assert.Single(readAgain).MessageId);
    }

    [Fact]
    public async Task ReadPendingAsync_should_only_return_new_messages_after_an_earlier_read()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([CreateMessage()]);
        await sut.ReadPendingAsync();

        var newMessage = CreateMessage();
        await sut.AppendAsync([newMessage]);
        var read = await sut.ReadPendingAsync();

        Assert.Equal(newMessage.MessageId, Assert.Single(read).MessageId);
    }
}
