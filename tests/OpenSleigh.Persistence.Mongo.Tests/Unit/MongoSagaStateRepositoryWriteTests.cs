using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo.Tests.Unit;

/// <summary>
/// what the repository does with the answers of the server, on a substitute of the collection: no server needed.
/// </summary>
public class MongoSagaStateRepositoryWriteTests
{
    [Fact]
    public async Task Lock_and_release_should_not_fail_when_writes_are_not_acknowledged()
    {
        var collection = CreateCollection(UpdateResult.Unacknowledged.Instance);
        var sut = CreateSut(collection);
        var saga = CreateSaga();

        await saga.LockAsync(sut, CancellationToken.None);
        await sut.ReleaseAsync(saga);

        Assert.False(string.IsNullOrEmpty(saga.LockId));
    }

    [Fact]
    public async Task ReleaseAsync_should_not_be_interrupted_once_it_started_writing()
    {
        using var stopping = new CancellationTokenSource();
        var collection = CreateCollection(new UpdateResult.Acknowledged(1, 1, null));
        var sut = CreateSut(collection);
        var saga = CreateSaga();
        await saga.LockAsync(sut, CancellationToken.None);
        collection.ClearReceivedCalls();
        collection
            .WhenForAnyArgs(c => c.UpdateOneAsync(default!, default!, default, default))
            .Do(_ => stopping.Cancel());

        await sut.ReleaseAsync(saga, stopping.Token);

        var release = Assert.Single(collection.ReceivedCalls());
        Assert.Equal(CancellationToken.None, release.GetArguments().OfType<CancellationToken>().Single());
    }

    [Fact]
    public async Task ReleaseAsync_should_write_nothing_when_it_is_already_cancelled()
    {
        var collection = CreateCollection(new UpdateResult.Acknowledged(1, 1, null));
        var sut = CreateSut(collection);
        var saga = CreateSaga();
        await saga.LockAsync(sut, CancellationToken.None);
        collection.ClearReceivedCalls();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sut.ReleaseAsync(saga, cancelled.Token));

        Assert.Empty(collection.ReceivedCalls());
    }

    [Fact]
    public async Task AbandonAsync_should_release_the_lock_and_write_nothing_of_the_saga()
    {
        var collection = CreateCollection(new UpdateResult.Acknowledged(1, 1, null));
        var sut = CreateSut(collection);
        var saga = CreateSaga();
        await saga.LockAsync(sut, CancellationToken.None);
        saga.MarkAsCompleted();
        collection.ClearReceivedCalls();

        await sut.AbandonAsync(saga);

        var release = Assert.Single(collection.ReceivedCalls());
        var update = release.GetArguments().OfType<UpdateDefinition<Entities.SagaState>>().Single();
        var registry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
        var written = update.Render(new RenderArgs<Entities.SagaState>(registry.GetSerializer<Entities.SagaState>(), registry))
            .AsBsonDocument["$set"].AsBsonDocument;
        Assert.Equal(
            new[] { nameof(Entities.SagaState.LockId), nameof(Entities.SagaState.LockTime) },
            written.Names.Order());
    }

    [Fact]
    public async Task ReleaseAsync_should_throw_when_the_state_is_null()
    {
        var sut = CreateSut(CreateCollection(UpdateResult.Unacknowledged.Instance));

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ReleaseAsync(null!));
    }

    private static IMongoCollection<Entities.SagaState> CreateCollection(UpdateResult updateResult)
    {
        var collection = Substitute.For<IMongoCollection<Entities.SagaState>>();
        collection.UpdateOneAsync(default!, default!, default, default).ReturnsForAnyArgs(updateResult);
        return collection;
    }

    private static MongoSagaStateRepository CreateSut(IMongoCollection<Entities.SagaState> collection)
    {
        var dbContext = Substitute.For<IDbContext>();
        dbContext.SagaStates.Returns(collection);
        return new MongoSagaStateRepository(dbContext, MongoSagaStateRepositoryOptions.Default, new JsonSerializer());
    }

    private static ISagaInstance CreateSaga()
        => new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            SagaDescriptor.Create<FakeSagaNoState>());
}
