using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OpenSleigh.Outbox;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit;

/// <summary>
/// how an outbox message is mapped and written, with the mappings the context registers: no server needed.
/// </summary>
public class OutboxMessageTests
{
    private const string DeliverAtElement = nameof(Entities.OutboxMessage.DeliverAt);

    private static readonly JsonSerializer Serializer = new();

    public OutboxMessageTests()
    {
        // registers the mappings, as an application does before it stores anything
        _ = new DbContext(Substitute.For<IMongoDatabase>());
    }

    [Fact]
    public void Create_should_leave_a_message_without_a_delay_without_a_due_time()
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), CreateSystemInfo());

        var entity = Entities.OutboxMessage.Create(envelope, Serializer);

        Assert.Null(entity.DeliverAt);
        Assert.True(entity.TryMap(CreateTypeResolver(), Serializer, out var mapped));
        Assert.Null(mapped.DeliverAt);
    }

    [Fact]
    public void Create_and_TryMap_should_keep_the_due_time_of_a_delayed_message()
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), CreateSystemInfo(), TimeSpan.FromMinutes(5));

        var entity = Entities.OutboxMessage.Create(envelope, Serializer);

        Assert.Equal(envelope.DeliverAt!.Value.UtcDateTime, entity.DeliverAt);
        Assert.Equal(DateTimeKind.Utc, entity.DeliverAt!.Value.Kind);
        Assert.True(entity.TryMap(CreateTypeResolver(), Serializer, out var mapped));
        Assert.Equal(envelope.DeliverAt, mapped.DeliverAt);
        Assert.Equal(TimeSpan.Zero, mapped.DeliverAt!.Value.Offset);
    }

    [Fact]
    public void A_message_without_a_delay_should_be_written_without_a_due_time_element()
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), CreateSystemInfo());

        var document = Entities.OutboxMessage.Create(envelope, Serializer).ToBsonDocument();

        Assert.False(document.Contains(DeliverAtElement));
    }

    [Fact]
    public void A_delayed_message_should_be_written_with_its_due_time_as_a_date_and_read_back()
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), CreateSystemInfo(), TimeSpan.FromMinutes(5));

        var document = Entities.OutboxMessage.Create(envelope, Serializer).ToBsonDocument();
        var read = BsonSerializer.Deserialize<Entities.OutboxMessage>(document);

        Assert.Equal(BsonType.DateTime, document[DeliverAtElement].BsonType);
        Assert.True(read.TryMap(CreateTypeResolver(), Serializer, out var mapped));
        // a date is stored to the millisecond
        Assert.Equal(envelope.DeliverAt!.Value, mapped.DeliverAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void A_document_stored_without_a_due_time_element_should_be_read_as_a_message_without_a_delay()
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), CreateSystemInfo(), TimeSpan.FromMinutes(5));
        var document = Entities.OutboxMessage.Create(envelope, Serializer).ToBsonDocument();
        document.Remove(DeliverAtElement);

        var read = BsonSerializer.Deserialize<Entities.OutboxMessage>(document);

        Assert.Null(read.DeliverAt);
        Assert.True(read.TryMap(CreateTypeResolver(), Serializer, out var mapped));
        Assert.Null(mapped.DeliverAt);
    }

    [Fact]
    public void A_document_with_an_element_this_version_does_not_know_should_still_be_read()
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), CreateSystemInfo());
        var document = Entities.OutboxMessage.Create(envelope, Serializer).ToBsonDocument();
        document.Add("AddedByALaterVersion", 42);

        var read = BsonSerializer.Deserialize<Entities.OutboxMessage>(document);

        Assert.Equal(envelope.MessageId, read.MessageId);
    }

    private static ISystemInfo CreateSystemInfo()
    {
        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.Id.Returns("sender");
        return systemInfo;
    }

    private static TypeResolver CreateTypeResolver()
    {
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeMessage));
        return typeResolver;
    }
}
