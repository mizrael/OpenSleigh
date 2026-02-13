using System.Linq;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Queries;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

public class SqlSagaStateQueryTests
{
    private static SagaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SagaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SagaDbContext(options);
    }

    private static SagaState CreateSagaStateEntity(
        string? instanceId = null,
        string? correlationId = null,
        string? sagaType = null,
        bool isCompleted = false,
        string? lockId = null,
        DateTimeOffset? lockTime = null,
        string? sagaStateType = null,
        byte[]? stateData = null)
    {
        return new SagaState
        {
            InstanceId = instanceId ?? Guid.NewGuid().ToString(),
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            TriggerMessageId = Guid.NewGuid().ToString(),
            SagaType = sagaType ?? "TestSaga",
            SagaStateType = sagaStateType,
            IsCompleted = isCompleted,
            LockId = lockId,
            LockTime = lockTime,
            StateData = stateData
        };
    }

    private static async Task SeedAsync(SagaDbContext db, params SagaState[] entities)
    {
        db.SagaStates.AddRange(entities);
        await db.SaveChangesAsync();
    }

    [Fact]
    public void Constructor_should_throw_on_null_dbContext()
    {
        var serializer = Substitute.For<ISerializer>();
        Assert.Throws<ArgumentNullException>(() => new SqlSagaStateQuery(null!, serializer));
    }

    [Fact]
    public void Constructor_should_throw_on_null_serializer()
    {
        using var db = CreateDbContext();
        Assert.Throws<ArgumentNullException>(() => new SqlSagaStateQuery(db, null!));
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_null_when_not_found()
    {
        using var db = CreateDbContext();
        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByInstanceIdAsync("non-existent-id");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_info_when_found()
    {
        using var db = CreateDbContext();
        var entity = CreateSagaStateEntity(instanceId: "instance-1", correlationId: "corr-1", sagaType: "MySaga");
        await SeedAsync(db, entity);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByInstanceIdAsync("instance-1");

        Assert.NotNull(result);
        Assert.Equal("instance-1", result!.InstanceId);
        Assert.Equal("corr-1", result.CorrelationId);
        Assert.Equal("MySaga", result.SagaType);
        Assert.Equal(entity.TriggerMessageId, result.TriggerMessageId);
        Assert.False(result.IsCompleted);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_include_processed_messages()
    {
        using var db = CreateDbContext();
        var entity = CreateSagaStateEntity(instanceId: "instance-pm");
        var msg1 = new SagaProcessedMessage
        {
            InstanceId = entity.InstanceId,
            MessageId = "msg-1",
            When = DateTimeOffset.UtcNow.AddMinutes(-5),
            SagaState = entity
        };
        var msg2 = new SagaProcessedMessage
        {
            InstanceId = entity.InstanceId,
            MessageId = "msg-2",
            When = DateTimeOffset.UtcNow,
            SagaState = entity
        };
        entity.ProcessedMessages.Add(msg1);
        entity.ProcessedMessages.Add(msg2);
        await SeedAsync(db, entity);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByInstanceIdAsync("instance-pm");

        Assert.NotNull(result);
        Assert.Equal(2, result!.ProcessedMessages.Count);
        Assert.Contains(result.ProcessedMessages, m => m.MessageId == "msg-1");
        Assert.Contains(result.ProcessedMessages, m => m.MessageId == "msg-2");
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_should_return_null_when_not_found()
    {
        using var db = CreateDbContext();
        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByCorrelationIdAsync("non-existent", "SomeType");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_should_return_info_when_found()
    {
        using var db = CreateDbContext();
        var entity = CreateSagaStateEntity(correlationId: "corr-find", sagaType: "FindSaga");
        await SeedAsync(db, entity);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByCorrelationIdAsync("corr-find", "FindSaga");

        Assert.NotNull(result);
        Assert.Equal("corr-find", result!.CorrelationId);
        Assert.Equal("FindSaga", result.SagaType);
        Assert.Equal(entity.InstanceId, result.InstanceId);
    }

    [Fact]
    public async Task GetAllAsync_should_return_all_items_with_default_filter()
    {
        using var db = CreateDbContext();
        var e1 = CreateSagaStateEntity(sagaType: "TypeA");
        var e2 = CreateSagaStateEntity(sagaType: "TypeB");
        var e3 = CreateSagaStateEntity(sagaType: "TypeA");
        await SeedAsync(db, e1, e2, e3);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetAllAsync(new SagaQueryFilter());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task GetAllAsync_should_filter_by_saga_type()
    {
        using var db = CreateDbContext();
        var e1 = CreateSagaStateEntity(sagaType: "Alpha");
        var e2 = CreateSagaStateEntity(sagaType: "Beta");
        var e3 = CreateSagaStateEntity(sagaType: "Alpha");
        await SeedAsync(db, e1, e2, e3);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetAllAsync(new SagaQueryFilter { SagaType = "Alpha" });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal("Alpha", i.SagaType));
    }

    [Fact]
    public async Task GetAllAsync_should_filter_by_completion_status()
    {
        using var db = CreateDbContext();
        var e1 = CreateSagaStateEntity(isCompleted: true);
        var e2 = CreateSagaStateEntity(isCompleted: false);
        var e3 = CreateSagaStateEntity(isCompleted: true);
        await SeedAsync(db, e1, e2, e3);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetAllAsync(new SagaQueryFilter { IsCompleted = true });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.True(i.IsCompleted));
    }

    [Fact]
    public async Task GetAllAsync_should_paginate_results()
    {
        using var db = CreateDbContext();
        var entities = Enumerable.Range(1, 5)
            .Select(i => CreateSagaStateEntity(instanceId: $"inst-{i:D3}"))
            .ToArray();
        await SeedAsync(db, entities);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var page1 = await sut.GetAllAsync(new SagaQueryFilter { Page = 1, PageSize = 2 });
        var page2 = await sut.GetAllAsync(new SagaQueryFilter { Page = 2, PageSize = 2 });
        var page3 = await sut.GetAllAsync(new SagaQueryFilter { Page = 3, PageSize = 2 });

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page1.PageSize);

        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(2, page2.Items.Count);

        Assert.Equal(5, page3.TotalCount);
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task GetAllAsync_should_use_default_filter_when_null()
    {
        using var db = CreateDbContext();
        var e1 = CreateSagaStateEntity();
        var e2 = CreateSagaStateEntity();
        await SeedAsync(db, e1, e2);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetAllAsync(null);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task MapToSagaInstanceInfo_should_deserialize_state_data()
    {
        using var db = CreateDbContext();
        var expectedState = DummyState.New();
        var serializer = new OpenSleigh.Utils.JsonSerializer();
        var stateBytes = serializer.Serialize(expectedState);
        var stateType = typeof(DummyState).AssemblyQualifiedName!;
        var entity = CreateSagaStateEntity(
            instanceId: "inst-deser",
            sagaStateType: stateType,
            stateData: stateBytes);
        await SeedAsync(db, entity);

        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByInstanceIdAsync("inst-deser");

        Assert.NotNull(result);
        Assert.NotNull(result!.StateData);
        var deserialized = Assert.IsType<DummyState>(result.StateData);
        Assert.Equal(expectedState.Id, deserialized.Id);
        Assert.Equal(expectedState.Foo, deserialized.Foo);
        Assert.Equal(expectedState.Bar, deserialized.Bar);
    }

    [Fact]
    public async Task MapToSagaInstanceInfo_should_handle_corrupted_state_data()
    {
        using var db = CreateDbContext();
        var stateBytes = new byte[] { 0xFF, 0xFE };
        var stateType = typeof(DummyState).AssemblyQualifiedName!;
        var entity = CreateSagaStateEntity(
            instanceId: "inst-corrupt",
            sagaStateType: stateType,
            stateData: stateBytes);
        await SeedAsync(db, entity);

        // Use real JsonSerializer which will throw on invalid bytes
        var serializer = new OpenSleigh.Utils.JsonSerializer();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByInstanceIdAsync("inst-corrupt");

        Assert.NotNull(result);
        Assert.Null(result!.StateData);
    }

    [Fact]
    public async Task MapToSagaInstanceInfo_should_detect_locked_state()
    {
        using var db = CreateDbContext();
        var entity = CreateSagaStateEntity(
            instanceId: "inst-locked",
            lockId: "lock-123",
            lockTime: DateTimeOffset.UtcNow);
        await SeedAsync(db, entity);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByInstanceIdAsync("inst-locked");

        Assert.NotNull(result);
        Assert.True(result!.IsLocked);
    }

    [Fact]
    public async Task MapToSagaInstanceInfo_should_detect_unlocked_state()
    {
        using var db = CreateDbContext();
        var entity = CreateSagaStateEntity(
            instanceId: "inst-unlocked",
            lockId: "lock-456",
            lockTime: null);
        await SeedAsync(db, entity);

        var serializer = Substitute.For<ISerializer>();
        var sut = new SqlSagaStateQuery(db, serializer);

        var result = await sut.GetByInstanceIdAsync("inst-unlocked");

        Assert.NotNull(result);
        Assert.False(result!.IsLocked);
    }
}
