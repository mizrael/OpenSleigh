using MongoDB.Bson;
using MongoDB.Bson.Serialization;
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
        => CreateMessage(TimeSpan.Zero);

    private static MessageEnvelope CreateMessage(TimeSpan delay)
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        sysInfo.ClientGroup.Returns("test");
        sysInfo.ClientId.Returns("client");
        sysInfo.Id.Returns("sender");
        return MessageEnvelope.Create(new FakeMessage(), sysInfo, delay);
    }

    private static MessageEnvelope CreateMessageDueSince(TimeSpan dueSince)
    {
        var serializer = new JsonSerializer();
        var now = DateTimeOffset.UtcNow;
        var created = MessageEnvelope.TryCreate(
            serializer.Serialize(new FakeMessage()),
            messageId: Guid.NewGuid().ToString("N"),
            correlationId: Guid.NewGuid().ToString("N"),
            createdAt: now - dueSince - TimeSpan.FromMinutes(1),
            typeof(FakeMessage),
            senderId: "sender",
            deliverAt: now - dueSince,
            serializer,
            out var message);
        Assert.True(created);
        return message!;
    }

    private MongoOutboxRepository CreateSut(IDbContext db)
        => CreateSut(db, MongoOutboxRepositoryOptions.Default);

    private static MongoOutboxRepository CreateSut(IDbContext db, MongoOutboxRepositoryOptions options)
    {
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeMessage));

        var sut = new MongoOutboxRepository(db, options, typeResolver, new JsonSerializer());
        return sut;
    }

    private static Task<Entities.OutboxMessage> FindStoredAsync(IDbContext db, MessageEnvelope message)
        => db.OutboxMessages.FindOneAsync(Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, message.MessageId));

    private static IMongoCollection<BsonDocument> RawOutbox(IDbContext db)
        => db.OutboxMessages.Database.GetCollection<BsonDocument>(db.OutboxMessages.CollectionNamespace.CollectionName);

    [Fact]
    public async Task ReadPendingAsync_should_neither_return_nor_claim_a_message_that_is_not_due()
    {
        var message = CreateMessage(TimeSpan.FromHours(1));
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var read = await sut.ReadPendingAsync();

        Assert.Empty(read);
        var stored = await FindStoredAsync(db, message);
        Assert.NotNull(stored);
        Assert.Null(stored.LockId);
        Assert.Null(stored.LockTime);
        Assert.NotNull(stored.DeliverAt);
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_a_message_whose_due_time_has_passed_with_its_due_time()
    {
        var message = CreateMessageDueSince(TimeSpan.FromHours(1));
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        await sut.AppendAsync([message]);

        var read = Assert.Single(await sut.ReadPendingAsync());

        Assert.Equal(message.MessageId, read.MessageId);
        Assert.NotNull(read.DeliverAt);
        // a date is stored to the millisecond
        Assert.Equal(message.DeliverAt!.Value, read.DeliverAt.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.Zero, read.DeliverAt.Value.Offset);
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_a_delayed_message_once_its_delay_has_elapsed()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        // created once the store is there: setting it up does not use up the delay
        var message = CreateMessage(TimeSpan.FromSeconds(1));
        await sut.AppendAsync([message]);

        var (read, readAt) = await ReadUntilAnyAsync(sut);

        Assert.Equal(message.MessageId, Assert.Single(read).MessageId);
        // every read that ended before the due time returned nothing. A date is stored to the millisecond
        Assert.True(
            readAt >= message.DeliverAt!.Value - StoredDatePrecision,
            $"read at {readAt:O}, before it was due at {message.DeliverAt:O}");
    }

    [Fact]
    public async Task ReadPendingAsync_should_only_return_the_due_and_the_immediate_messages_read_after_read()
    {
        var immediate = CreateMessage();
        var due = CreateMessageDueSince(TimeSpan.FromHours(1));
        var notDue = CreateMessage(TimeSpan.FromHours(1));
        var notDueEither = CreateMessage(TimeSpan.FromDays(1));
        var expected = new[] { immediate.MessageId, due.MessageId }.Order();
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db, new MongoOutboxRepositoryOptions(TimeSpan.Zero));
        await sut.AppendAsync([notDue, immediate, notDueEither, due]);

        var read = await sut.ReadPendingAsync();
        // the claim of the first read expires as soon as the clock moves
        var (readAgain, _) = await ReadUntilAnyAsync(sut);

        Assert.Equal(expected, read.Select(m => m.MessageId).Order());
        Assert.Equal(expected, readAgain.Select(m => m.MessageId).Order());
        Assert.Null((await FindStoredAsync(db, notDue)).LockId);
        Assert.Null((await FindStoredAsync(db, notDueEither)).LockId);
    }

    private static readonly TimeSpan StoredDatePrecision = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PauseBetweenReads = TimeSpan.FromMilliseconds(20);

    // reads until messages are returned, and tells when the read that returned them ended
    private static async Task<(IReadOnlyList<MessageEnvelope> Messages, DateTimeOffset ReadAt)> ReadUntilAnyAsync(MongoOutboxRepository sut)
    {
        var giveUpAt = DateTimeOffset.UtcNow + ReadLimit;

        while (true)
        {
            var messages = (await sut.ReadPendingAsync()).ToList();
            var readAt = DateTimeOffset.UtcNow;
            if (messages.Count > 0)
                return (messages, readAt);

            Assert.True(readAt < giveUpAt, $"no message was returned in {ReadLimit}");
            await Task.Delay(PauseBetweenReads);
        }
    }

    [Fact]
    public async Task AppendAsync_should_store_a_message_without_a_delay_without_a_due_time_element()
    {
        var message = CreateMessage();
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([message]);

        var document = await RawOutbox(db).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.False(document.Contains(DeliverAtElement));
    }

    [Fact]
    public async Task ReadPendingAsync_should_return_a_message_stored_before_there_were_due_times()
    {
        var message = CreateMessage();
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var document = Entities.OutboxMessage.Create(message, new JsonSerializer()).ToBsonDocument();
        document.Remove(DeliverAtElement);
        await RawOutbox(db).InsertOneAsync(document);

        var read = Assert.Single(await sut.ReadPendingAsync());

        Assert.Equal(message.MessageId, read.MessageId);
        Assert.Null(read.DeliverAt);
    }

    [Fact]
    public async Task AppendAsync_should_store_the_due_time_of_a_delayed_message_as_a_date()
    {
        var message = CreateMessage(TimeSpan.FromHours(1));
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await sut.AppendAsync([message]);

        var document = await RawOutbox(db).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(BsonType.DateTime, document[DeliverAtElement].BsonType);
    }

    [Fact]
    public async Task ReadPendingAsync_should_look_the_due_messages_up_through_the_index_whatever_is_waiting_for_its_time()
    {
        const int notDueCount = 300;
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);
        var dueMessages = new[]
        {
            CreateMessage(), CreateMessage(), CreateMessage(),
            CreateMessageDueSince(TimeSpan.FromHours(1)), CreateMessageDueSince(TimeSpan.FromSeconds(1))
        };
        await sut.AppendAsync(Enumerable.Range(0, notDueCount).Select(_ => CreateMessage(TimeSpan.FromHours(1))));
        await sut.AppendAsync(dueMessages);

        var now = DateTimeOffset.UtcNow;
        var claim = await ExplainAsync(db, MongoOutboxRepository.Available(now, MongoOutboxRepositoryOptions.Default.LockMaxDuration));

        var read = await sut.ReadPendingAsync();
        var claimId = (await FindStoredAsync(db, dueMessages[0])).LockId;
        Assert.NotNull(claimId);
        var readClaimed = await ExplainAsync(db, MongoOutboxRepository.ClaimedBy(claimId, DateTimeOffset.UtcNow));

        Assert.Equal(dueMessages.Length, read.Count());
        AssertAnsweredFromTheDueTimeIndex(claim, dueMessages.Length);
        AssertAnsweredFromTheDueTimeIndex(readClaimed, dueMessages.Length);
    }

    private static void AssertAnsweredFromTheDueTimeIndex(BsonDocument explained, int dueCount)
    {
        var stages = Stages(explained[QueryPlannerElement][WinningPlanElement]).ToArray();
        Assert.DoesNotContain(stages, stage => stage[StageElement] == CollectionScanStage);
        Assert.Contains(stages, stage =>
            stage[StageElement] == IndexScanStage &&
            stage[IndexNameElement] == DbContext.OutboxDeliverAtIndexName);

        var executionStats = explained[ExecutionStatsElement];
        Assert.Equal(dueCount, executionStats[ReturnedElement].ToInt32());
        // nothing that is not due is looked at. Some slack, in case the server counts a boundary document more
        Assert.InRange(executionStats[DocsExaminedElement].ToInt32(), dueCount, dueCount * 2);
    }

    private static async Task<BsonDocument> ExplainAsync(IDbContext db, FilterDefinition<Entities.OutboxMessage> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        var rendered = filter.Render(new RenderArgs<Entities.OutboxMessage>(registry.GetSerializer<Entities.OutboxMessage>(), registry));
        var command = new BsonDocument
        {
            {
                "explain", new BsonDocument
                {
                    { "find", db.OutboxMessages.CollectionNamespace.CollectionName },
                    { "filter", rendered }
                }
            },
            { "verbosity", "executionStats" }
        };

        return await db.OutboxMessages.Database.RunCommandAsync<BsonDocument>(command);
    }

    // the plan is a tree of stages, nested under different elements depending on the stage and on the server version
    private static IEnumerable<BsonDocument> Stages(BsonValue plan)
    {
        if (plan is BsonArray array)
            return array.SelectMany(Stages);

        if (plan is not BsonDocument document)
            return [];

        var nested = document.Values.SelectMany(Stages);
        return document.Contains(StageElement) ? nested.Prepend(document) : nested;
    }

    private const string DeliverAtElement = nameof(Entities.OutboxMessage.DeliverAt);
    private const string QueryPlannerElement = "queryPlanner";
    private const string WinningPlanElement = "winningPlan";
    private const string ExecutionStatsElement = "executionStats";
    private const string ReturnedElement = "nReturned";
    private const string DocsExaminedElement = "totalDocsExamined";
    private const string StageElement = "stage";
    private const string IndexNameElement = "indexName";
    private const string IndexScanStage = "IXSCAN";
    private const string CollectionScanStage = "COLLSCAN";

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
