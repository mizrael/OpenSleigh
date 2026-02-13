using OpenSleigh.InMemory;
using OpenSleigh.Queries;

namespace OpenSleigh.Tests.Queries;

public class InMemorySagaStateQueryTests
{
    private readonly InMemorySagaStateRepository _repo = new();
    private ISagaStateQuery Query => _repo;

    private static SagaInstance CreateInstance(
        string? instanceId = null,
        string? correlationId = null,
        SagaDescriptor? descriptor = null)
    {
        return new SagaInstance(
            instanceId ?? Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            correlationId ?? Guid.NewGuid().ToString(),
            descriptor ?? SagaDescriptor.Create<FakeSaga>());
    }

    private static SagaInstance<int> CreateStatefulInstance(
        int state,
        string? instanceId = null,
        string? correlationId = null)
    {
        return new SagaInstance<int>(
            instanceId ?? Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            correlationId ?? Guid.NewGuid().ToString(),
            SagaDescriptor.Create<FakeSagaWithState, int>(),
            state);
    }

    private async Task StoreAndRelease(ISagaInstance instance)
    {
        await _repo.LockAsync(instance);
        await _repo.ReleaseAsync(instance);
    }

    #region GetByInstanceIdAsync

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_info_when_instance_exists()
    {
        var instance = CreateInstance(instanceId: "inst-1", correlationId: "corr-1");
        await StoreAndRelease(instance);

        var result = await Query.GetByInstanceIdAsync("inst-1");

        Assert.NotNull(result);
        Assert.Equal("inst-1", result!.InstanceId);
        Assert.Equal("corr-1", result.CorrelationId);
        Assert.Equal(instance.TriggerMessageId, result.TriggerMessageId);
        Assert.Equal(typeof(FakeSaga).FullName, result.SagaType);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_null_when_not_found()
    {
        var result = await Query.GetByInstanceIdAsync("non-existent");

        Assert.Null(result);
    }

    #endregion

    #region GetByCorrelationIdAsync

    [Fact]
    public async Task GetByCorrelationIdAsync_should_return_info_matching_correlation_and_sagaType()
    {
        var instance = CreateInstance(correlationId: "corr-2");
        await StoreAndRelease(instance);

        var result = await Query.GetByCorrelationIdAsync("corr-2", typeof(FakeSaga).FullName!);

        Assert.NotNull(result);
        Assert.Equal("corr-2", result!.CorrelationId);
        Assert.Equal(instance.InstanceId, result.InstanceId);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_should_return_null_when_correlation_not_found()
    {
        var instance = CreateInstance(correlationId: "corr-3");
        await StoreAndRelease(instance);

        var result = await Query.GetByCorrelationIdAsync("wrong-corr", typeof(FakeSaga).FullName!);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_should_return_null_when_sagaType_does_not_match()
    {
        var instance = CreateInstance(correlationId: "corr-4");
        await StoreAndRelease(instance);

        var result = await Query.GetByCorrelationIdAsync("corr-4", typeof(FakeSagaWithState).FullName!);

        Assert.Null(result);
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_should_return_all_instances()
    {
        await StoreAndRelease(CreateInstance());
        await StoreAndRelease(CreateInstance());
        await StoreAndRelease(CreateInstance());

        var result = await Query.GetAllAsync();

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task GetAllAsync_should_filter_by_SagaType()
    {
        await StoreAndRelease(CreateInstance());
        await StoreAndRelease(CreateStatefulInstance(state: 10));

        var filter = new SagaQueryFilter { SagaType = typeof(FakeSagaWithState).FullName };
        var result = await Query.GetAllAsync(filter);

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(typeof(FakeSagaWithState).FullName, item.SagaType));
    }

    [Fact]
    public async Task GetAllAsync_should_filter_by_IsCompleted()
    {
        var completedInstance = CreateInstance();
        completedInstance.MarkAsCompleted();
        await StoreAndRelease(completedInstance);

        await StoreAndRelease(CreateInstance());

        var filter = new SagaQueryFilter { IsCompleted = true };
        var result = await Query.GetAllAsync(filter);

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Items, item => Assert.True(item.IsCompleted));
    }

    [Fact]
    public async Task GetAllAsync_should_paginate_correctly()
    {
        for (int i = 0; i < 5; i++)
            await StoreAndRelease(CreateInstance());

        var page1 = await Query.GetAllAsync(new SagaQueryFilter { Page = 1, PageSize = 2 });
        var page2 = await Query.GetAllAsync(new SagaQueryFilter { Page = 2, PageSize = 2 });
        var page3 = await Query.GetAllAsync(new SagaQueryFilter { Page = 3, PageSize = 2 });

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(1, page1.Page);

        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(2, page2.Page);

        Assert.Equal(5, page3.TotalCount);
        Assert.Single(page3.Items);
        Assert.Equal(3, page3.Page);
    }

    #endregion

    #region StateData

    [Fact]
    public async Task GetByInstanceIdAsync_should_include_state_data_for_stateful_sagas()
    {
        var instance = CreateStatefulInstance(state: 42, instanceId: "stateful-1");
        await StoreAndRelease(instance);

        var result = await Query.GetByInstanceIdAsync("stateful-1");

        Assert.NotNull(result);
        Assert.NotNull(result!.StateData);
        Assert.Equal(42, (int)result.StateData!);
        Assert.Equal(typeof(int).FullName, result.SagaStateType);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_return_null_state_data_for_stateless_sagas()
    {
        var instance = CreateInstance(instanceId: "stateless-1");
        await StoreAndRelease(instance);

        var result = await Query.GetByInstanceIdAsync("stateless-1");

        Assert.NotNull(result);
        Assert.Null(result!.StateData);
        Assert.Null(result.SagaStateType);
    }

    #endregion

    #region IsLocked

    [Fact]
    public async Task GetByInstanceIdAsync_should_reflect_locked_state_when_locked()
    {
        var instance = CreateInstance(instanceId: "locked-1");
        await _repo.LockAsync(instance);

        var result = await Query.GetByInstanceIdAsync("locked-1");

        Assert.NotNull(result);
        Assert.True(result!.IsLocked);
    }

    [Fact]
    public async Task GetByInstanceIdAsync_should_reflect_unlocked_state_after_release()
    {
        var instance = CreateInstance(instanceId: "unlocked-1");
        await StoreAndRelease(instance);

        var result = await Query.GetByInstanceIdAsync("unlocked-1");

        Assert.NotNull(result);
        Assert.False(result!.IsLocked);
    }

    #endregion
}
