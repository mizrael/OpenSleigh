using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public class MongoDbContextTests : IClassFixture<DbFixture>
{
    private const string OutboxMessageIdIndex = "ix_message_id";
    private const string OutboxDeliverAtIndex = "ix_deliver_at";
    private const string SagaCorrelationIndex = "ix_correlation_type";
    private const string SagaInstanceIdIndex = "ix_instance_id";

    private readonly DbFixture _fixture;

    public MongoDbContextTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Ctor_should_create_the_indexes()
    {
        var sut = _fixture.CreateDbContext();

        var outboxIndexes = await ReadIndexesAsync(sut.OutboxMessages);
        var sagaIndexes = await ReadIndexesAsync(sut.SagaStates);

        Assert.True(IsUnique(outboxIndexes[OutboxMessageIdIndex]));
        Assert.True(IsUnique(sagaIndexes[SagaCorrelationIndex]));
        Assert.False(IsUnique(sagaIndexes[SagaInstanceIdIndex]));
    }

    [Fact]
    public async Task Ctor_should_create_an_index_on_the_due_time_that_also_covers_the_messages_without_one()
    {
        var sut = _fixture.CreateDbContext();

        var outboxIndexes = await ReadIndexesAsync(sut.OutboxMessages);

        var index = outboxIndexes[OutboxDeliverAtIndex];
        Assert.Equal(new BsonDocument(nameof(Entities.OutboxMessage.DeliverAt), 1), index["key"]);
        Assert.False(IsUnique(index));
        Assert.False(index.Contains("sparse"));
        Assert.False(index.Contains("partialFilterExpression"));
    }

    [Fact]
    public async Task Ctor_should_accept_an_existing_index_on_the_due_time_under_another_name()
    {
        var database = new MongoClient(_fixture.ConnectionString).GetDatabase(_fixture.CreateDatabaseName());
        var outbox = database.GetCollection<Entities.OutboxMessage>("outbox");
        await outbox.Indexes.CreateOneAsync(new CreateIndexModel<Entities.OutboxMessage>(
            Builders<Entities.OutboxMessage>.IndexKeys.Ascending(e => e.DeliverAt),
            new CreateIndexOptions { Name = "created_by_someone_else" }));

        var sut = new DbContext(database);

        var outboxIndexes = await ReadIndexesAsync(sut.OutboxMessages);
        Assert.True(outboxIndexes.ContainsKey("created_by_someone_else"));
        Assert.False(outboxIndexes.ContainsKey(OutboxDeliverAtIndex));
        Assert.True(IsUnique(outboxIndexes[OutboxMessageIdIndex]));
    }

    [Fact]
    public async Task UseMongoPersistence_should_share_one_context_and_create_the_indexes()
    {
        var configuration = new MongoConfiguration(_fixture.ConnectionString, _fixture.CreateDatabaseName());
        await using var provider = BuildProvider(configuration);

        IDbContext first;
        IDbContext second;
        using (var scope = provider.CreateScope())
            first = scope.ServiceProvider.GetRequiredService<IDbContext>();
        using (var scope = provider.CreateScope())
            second = scope.ServiceProvider.GetRequiredService<IDbContext>();

        Assert.Same(first, second);
        var outboxIndexes = await ReadIndexesAsync(first.OutboxMessages);
        Assert.True(IsUnique(outboxIndexes[OutboxMessageIdIndex]));
    }

    [Fact]
    public async Task New_service_provider_should_recreate_the_indexes_of_a_dropped_database()
    {
        var configuration = new MongoConfiguration(_fixture.ConnectionString, _fixture.CreateDatabaseName());

        await using var provider = BuildProvider(configuration);
        var context = provider.GetRequiredService<IDbContext>();
        await context.OutboxMessages.Database.Client.DropDatabaseAsync(configuration.DbName);

        await using var newProvider = BuildProvider(configuration);
        var newContext = newProvider.GetRequiredService<IDbContext>();

        var outboxIndexes = await ReadIndexesAsync(newContext.OutboxMessages);
        Assert.True(outboxIndexes.ContainsKey(OutboxMessageIdIndex), "the unique outbox index was not recreated");
        Assert.True(IsUnique(outboxIndexes[OutboxMessageIdIndex]));
    }

    [Fact]
    public async Task Ctor_should_accept_an_existing_index_on_the_instance_id_under_another_name()
    {
        var database = new MongoClient(_fixture.ConnectionString).GetDatabase(_fixture.CreateDatabaseName());
        var sagaStates = database.GetCollection<Entities.SagaState>("sagaStates");
        await sagaStates.Indexes.CreateOneAsync(new CreateIndexModel<Entities.SagaState>(
            Builders<Entities.SagaState>.IndexKeys.Ascending(e => e.InstanceId),
            new CreateIndexOptions { Name = "created_by_someone_else" }));

        var sut = new DbContext(database);

        var sagaIndexes = await ReadIndexesAsync(sut.SagaStates);
        Assert.True(sagaIndexes.ContainsKey("created_by_someone_else"));
        Assert.False(sagaIndexes.ContainsKey(SagaInstanceIdIndex));
        Assert.True(IsUnique(sagaIndexes[SagaCorrelationIndex]));
    }

    private static ServiceProvider BuildProvider(MongoConfiguration configuration)
        => new ServiceCollection()
            .AddOpenSleigh(configurator => configurator.UseMongoPersistence(configuration))
            .BuildServiceProvider();

    private static async Task<Dictionary<string, BsonDocument>> ReadIndexesAsync<T>(IMongoCollection<T> collection)
    {
        var cursor = await collection.Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();
        return indexes.ToDictionary(index => index["name"].AsString);
    }

    private static bool IsUnique(BsonDocument index)
        => index.TryGetValue("unique", out var unique) && unique.ToBoolean();
}
