using MongoDB.Bson;
using OpenSleigh.Persistence.Mongo.Entities;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using OpenSleigh.Queries;
using System.ComponentModel;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public class MongoSagaStateQueryTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;
    private readonly ISerializer _serializer = new JsonSerializer();

    public MongoSagaStateQueryTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    private MongoSagaStateQuery CreateSut(IDbContext db)
        => new MongoSagaStateQuery(db, _serializer);

    private async Task<Entities.SagaState> InsertSagaState(
        IDbContext db,
        string? instanceId = null,
        string? correlationId = null,
        string? sagaType = null,
        string? sagaStateType = null,
        byte[]? stateData = null,
        bool isCompleted = false,
        string? lockId = null,
        DateTimeOffset? lockTime = null,
        ICollection<SagaProcessedMessage>? processedMessages = null)
    {
        var entity = new Entities.SagaState
        {
            Id = ObjectId.GenerateNewId(),
            InstanceId = instanceId ?? Guid.NewGuid().ToString(),
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            TriggerMessageId = Guid.NewGuid().ToString(),
            SagaType = sagaType ?? typeof(FakeSagaNoState).FullName!,
            SagaStateType = sagaStateType,
            StateData = stateData,
            IsCompleted = isCompleted,
            LockId = lockId,
            LockTime = lockTime,
            ProcessedMessages = processedMessages ?? new List<SagaProcessedMessage>()
        };

        await db.SagaStates.InsertOneAsync(entity);
        return entity;
    }

    #region Constructor

    [Fact]
    public void ctor_should_throw_when_dbContext_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new MongoSagaStateQuery(null!, _serializer));
    }

    [Fact]
    public void ctor_should_throw_when_serializer_is_null()
    {
        var db = _fixture.CreateDbContext();
        Assert.Throws<ArgumentNullException>(() => new MongoSagaStateQuery(db, null!));
    }

    #endregion

    #region GetByInstanceIdAsync

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_null_when_not_found()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.GetByInstanceIdAsync(Guid.NewGuid().ToString());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_mapped_instance()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var entity = await InsertSagaState(db);

        var result = await sut.GetByInstanceIdAsync(entity.InstanceId);

        Assert.NotNull(result);
        Assert.Equal(entity.InstanceId, result.InstanceId);
        Assert.Equal(entity.CorrelationId, result.CorrelationId);
        Assert.Equal(entity.TriggerMessageId, result.TriggerMessageId);
        Assert.Equal(entity.SagaType, result.SagaType);
        Assert.False(result.IsCompleted);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_deserialize_state_data()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var state = DummyState.New();
        var stateData = _serializer.Serialize(state);

        var entity = await InsertSagaState(db,
            sagaStateType: typeof(DummyState).AssemblyQualifiedName,
            stateData: stateData);

        var result = await sut.GetByInstanceIdAsync(entity.InstanceId);

        Assert.NotNull(result);
        Assert.NotNull(result.StateData);
        var deserialized = Assert.IsType<DummyState>(result.StateData);
        Assert.Equal(state.Id, deserialized.Id);
        Assert.Equal(state.Foo, deserialized.Foo);
        Assert.Equal(state.Bar, deserialized.Bar);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_null_state_when_data_corrupted()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var entity = await InsertSagaState(db,
            sagaStateType: typeof(DummyState).AssemblyQualifiedName,
            stateData: new byte[] { 0xFF, 0xFE });

        var result = await sut.GetByInstanceIdAsync(entity.InstanceId);

        Assert.NotNull(result);
        Assert.Null(result.StateData);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_null_state_when_type_unknown()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var entity = await InsertSagaState(db,
            sagaStateType: "Some.NonExistent.Type, FakeAssembly",
            stateData: new byte[] { 0x01 });

        var result = await sut.GetByInstanceIdAsync(entity.InstanceId);

        Assert.NotNull(result);
        Assert.Null(result.StateData);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_detect_locked_state()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var entity = await InsertSagaState(db,
            lockId: Guid.NewGuid().ToString(),
            lockTime: DateTimeOffset.UtcNow);

        var result = await sut.GetByInstanceIdAsync(entity.InstanceId);

        Assert.NotNull(result);
        Assert.True(result.IsLocked);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_include_processed_messages()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var instanceId = Guid.NewGuid().ToString();
        var messages = new List<SagaProcessedMessage>
        {
            new() { InstanceId = instanceId, MessageId = Guid.NewGuid().ToString(), When = DateTimeOffset.UtcNow },
            new() { InstanceId = instanceId, MessageId = Guid.NewGuid().ToString(), When = DateTimeOffset.UtcNow }
        };

        var entity = await InsertSagaState(db,
            instanceId: instanceId,
            processedMessages: messages);

        var result = await sut.GetByInstanceIdAsync(entity.InstanceId);

        Assert.NotNull(result);
        Assert.Equal(2, result.ProcessedMessages.Count);
        Assert.Contains(result.ProcessedMessages, m => m.MessageId == messages[0].MessageId);
        Assert.Contains(result.ProcessedMessages, m => m.MessageId == messages[1].MessageId);
    }

    #endregion

    #region GetByCorrelationIdAsync

    [Fact]
    public async Task GetByCorrelationIdAsync_should_return_null_when_not_found()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.GetByCorrelationIdAsync(
            Guid.NewGuid().ToString(),
            typeof(FakeSagaNoState).FullName!);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_should_return_matching_instance()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var correlationId = Guid.NewGuid().ToString();
        var sagaType = typeof(FakeSagaNoState).FullName!;

        var entity = await InsertSagaState(db,
            correlationId: correlationId,
            sagaType: sagaType);

        var result = await sut.GetByCorrelationIdAsync(correlationId, sagaType);

        Assert.NotNull(result);
        Assert.Equal(entity.InstanceId, result.InstanceId);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(sagaType, result.SagaType);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_should_not_return_different_saga_type()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var correlationId = Guid.NewGuid().ToString();

        await InsertSagaState(db,
            correlationId: correlationId,
            sagaType: typeof(FakeSagaNoState).FullName!);

        var result = await sut.GetByCorrelationIdAsync(correlationId, "Some.Other.SagaType");

        Assert.Null(result);
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_should_return_empty_when_no_data()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.GetAllAsync();

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetAllAsync_should_return_all_items_with_default_filter()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await InsertSagaState(db);
        await InsertSagaState(db);
        await InsertSagaState(db);

        var result = await sut.GetAllAsync();

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task GetAllAsync_should_filter_by_saga_type()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var targetType = typeof(FakeSagaWithState).FullName!;
        var otherType = typeof(FakeSagaNoState).FullName!;

        await InsertSagaState(db, sagaType: targetType);
        await InsertSagaState(db, sagaType: targetType);
        await InsertSagaState(db, sagaType: otherType);

        var filter = new SagaQueryFilter { SagaType = targetType };
        var result = await sut.GetAllAsync(filter);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(targetType, item.SagaType));
    }

    [Fact]
    public async Task GetAllAsync_should_filter_by_is_completed()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await InsertSagaState(db, isCompleted: true);
        await InsertSagaState(db, isCompleted: true);
        await InsertSagaState(db, isCompleted: false);

        var filter = new SagaQueryFilter { IsCompleted = true };
        var result = await sut.GetAllAsync(filter);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.True(item.IsCompleted));
    }

    [Fact]
    public async Task GetAllAsync_should_paginate_results()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        for (int i = 0; i < 5; i++)
            await InsertSagaState(db);

        var filter = new SagaQueryFilter { Page = 1, PageSize = 2 };
        var result = await sut.GetAllAsync(filter);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
    }

    [Fact]
    public async Task GetAllAsync_should_return_correct_second_page()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        for (int i = 0; i < 5; i++)
            await InsertSagaState(db);

        var page1 = await sut.GetAllAsync(new SagaQueryFilter { Page = 1, PageSize = 3 });
        var page2 = await sut.GetAllAsync(new SagaQueryFilter { Page = 2, PageSize = 3 });

        Assert.Equal(3, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(5, page2.TotalCount);

        var allIds = page1.Items.Select(i => i.InstanceId)
            .Concat(page2.Items.Select(i => i.InstanceId))
            .ToList();
        Assert.Equal(5, allIds.Distinct().Count());
    }

    [Fact]
    public async Task GetAllAsync_should_combine_filters()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var targetType = typeof(FakeSagaWithState).FullName!;

        await InsertSagaState(db, sagaType: targetType, isCompleted: true);
        await InsertSagaState(db, sagaType: targetType, isCompleted: false);
        await InsertSagaState(db, sagaType: typeof(FakeSagaNoState).FullName!, isCompleted: true);

        var filter = new SagaQueryFilter { SagaType = targetType, IsCompleted = true };
        var result = await sut.GetAllAsync(filter);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(targetType, result.Items[0].SagaType);
        Assert.True(result.Items[0].IsCompleted);
    }

    [Fact]
    public async Task GetAllAsync_should_use_default_filter_when_null()
    {
        var db = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        await InsertSagaState(db);
        await InsertSagaState(db);

        var result = await sut.GetAllAsync(null);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    #endregion
}
