using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OpenSleigh.Outbox;
using System.Linq;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit;

/// <summary>
/// what the outbox filters look like once they are sent to the server: no server needed.
/// </summary>
public class OutboxFiltersTests
{
    private const string DeliverAtElement = nameof(Entities.OutboxMessage.DeliverAt);
    private const string MessageIdElement = nameof(Entities.OutboxMessage.MessageId);
    private const string LockIdElement = nameof(Entities.OutboxMessage.LockId);

    public OutboxFiltersTests()
    {
        _ = new DbContext(Substitute.For<IMongoDatabase>());
    }

    [Fact]
    public void Due_should_be_one_condition_on_the_due_time_that_also_holds_without_one()
    {
        var now = DateTimeOffset.UtcNow;

        var rendered = Render(MongoOutboxRepository.Due(now));

        var expected = new BsonDocument(DeliverAtElement,
            new BsonDocument("$not", new BsonDocument("$gt", new BsonDateTime(now.UtcDateTime))));
        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void Available_and_ClaimedBy_should_both_be_restricted_to_the_due_messages()
    {
        var now = DateTimeOffset.UtcNow;
        var due = Render(MongoOutboxRepository.Due(now))[DeliverAtElement];

        var available = Render(MongoOutboxRepository.Available(now, TimeSpan.FromMinutes(1)));
        var claimed = Render(MongoOutboxRepository.ClaimedBy("claim", now));

        Assert.Equal(due, available[DeliverAtElement]);
        Assert.Equal(due, claimed[DeliverAtElement]);
        Assert.Equal("claim", claimed[LockIdElement].AsString);
    }

    [Fact]
    public async Task CommitAsync_should_look_its_held_messages_up_by_message_id_and_hold()
    {
        var sagaStates = Substitute.For<IMongoCollection<Entities.SagaState>>();
        sagaStates.UpdateOneAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(new UpdateResult.Acknowledged(1, 1, null));
        var outbox = Substitute.For<IMongoCollection<Entities.OutboxMessage>>();
        var dbContext = Substitute.For<IDbContext>();
        dbContext.SagaStates.Returns(sagaStates);
        dbContext.OutboxMessages.Returns(outbox);
        var sut = new MongoSagaStateRepository(dbContext, MongoSagaStateRepositoryOptions.Default, new JsonSerializer());
        var saga = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            SagaDescriptor.Create<FakeSagaNoState>());
        await saga.LockAsync(sut, CancellationToken.None);
        var immediate = MessageEnvelope.Create(new FakeMessage(), saga);
        var delayed = MessageEnvelope.Create(new FakeMessage(), saga, TimeSpan.FromHours(1));
        saga.Publish(immediate);
        saga.Publish(delayed);

        await sut.CommitAsync(saga);

        var letGo = Assert.Single(outbox.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IMongoCollection<Entities.OutboxMessage>.UpdateManyAsync));
        var filter = Render(letGo.GetArguments().OfType<FilterDefinition<Entities.OutboxMessage>>().Single());
        Assert.Equal(
            new[] { immediate.MessageId, delayed.MessageId }.Order(),
            filter[MessageIdElement]["$in"].AsBsonArray.Select(id => id.AsString).Order());
        Assert.False(string.IsNullOrEmpty(filter[LockIdElement].AsString));
    }

    private static BsonDocument Render(FilterDefinition<Entities.OutboxMessage> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return filter.Render(new RenderArgs<Entities.OutboxMessage>(registry.GetSerializer<Entities.OutboxMessage>(), registry));
    }
}
