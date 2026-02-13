using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenSleigh.Queries;
using OpenSleigh.Reporting.Endpoints;

namespace OpenSleigh.Reporting.Tests;

public class SagaEndpointsTests
{
    private readonly ISagaStateQuery _query = Substitute.For<ISagaStateQuery>();
    private readonly ISagaDescriptorsResolver _resolver = Substitute.For<ISagaDescriptorsResolver>();

    [Fact]
    public async Task GetAll_should_return_Ok_with_paginated_results()
    {
        var items = new List<SagaInstanceInfo>
        {
            CreateSagaInstanceInfo("id-1"),
            CreateSagaInstanceInfo("id-2")
        };
        var pagedResult = new PagedResult<SagaInstanceInfo>(items, TotalCount: 2, Page: 1, PageSize: 20);
        _query.GetAllAsync(Arg.Any<SagaQueryFilter>(), Arg.Any<CancellationToken>())
            .Returns(pagedResult);

        var result = await SagaEndpoints.GetAll(_query, sagaType: null, isCompleted: null);

        var okResult = Assert.IsType<Ok<PagedResult<SagaInstanceInfo>>>(result);
        Assert.Equal(2, okResult.Value!.TotalCount);
        Assert.Equal(2, okResult.Value.Items.Count);
    }

    [Fact]
    public async Task GetAll_should_pass_filter_parameters_to_query()
    {
        var pagedResult = new PagedResult<SagaInstanceInfo>([], TotalCount: 0, Page: 2, PageSize: 10);
        _query.GetAllAsync(Arg.Any<SagaQueryFilter>(), Arg.Any<CancellationToken>())
            .Returns(pagedResult);

        await SagaEndpoints.GetAll(_query, sagaType: "MySaga", isCompleted: true, page: 2, pageSize: 10);

        await _query.Received(1).GetAllAsync(
            Arg.Is<SagaQueryFilter>(f =>
                f.SagaType == "MySaga" &&
                f.IsCompleted == true &&
                f.Page == 2 &&
                f.PageSize == 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByInstanceId_should_return_Ok_when_found()
    {
        var info = CreateSagaInstanceInfo("id-1");
        _query.GetByInstanceIdAsync("id-1", Arg.Any<CancellationToken>())
            .Returns(info);

        var result = await SagaEndpoints.GetByInstanceId(_query, "id-1");

        var okResult = Assert.IsType<Ok<SagaInstanceInfo>>(result);
        Assert.Equal("id-1", okResult.Value!.InstanceId);
    }

    [Fact]
    public async Task GetByInstanceId_should_return_NotFound_when_not_found()
    {
        _query.GetByInstanceIdAsync("missing", Arg.Any<CancellationToken>())
            .Returns((SagaInstanceInfo?)null);

        var result = await SagaEndpoints.GetByInstanceId(_query, "missing");

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetByCorrelationId_should_return_Ok_when_found()
    {
        var info = CreateSagaInstanceInfo("id-1", correlationId: "corr-1");
        _query.GetByCorrelationIdAsync("corr-1", "MySaga", Arg.Any<CancellationToken>())
            .Returns(info);

        var result = await SagaEndpoints.GetByCorrelationId(_query, "corr-1", "MySaga");

        var okResult = Assert.IsType<Ok<SagaInstanceInfo>>(result);
        Assert.Equal("corr-1", okResult.Value!.CorrelationId);
    }

    [Fact]
    public async Task GetByCorrelationId_should_return_NotFound_when_not_found()
    {
        _query.GetByCorrelationIdAsync("missing", "MySaga", Arg.Any<CancellationToken>())
            .Returns((SagaInstanceInfo?)null);

        var result = await SagaEndpoints.GetByCorrelationId(_query, "missing", "MySaga");

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public void GetRegisteredTypes_should_return_Ok_with_message_types()
    {
        _resolver.GetRegisteredMessageTypes()
            .Returns(new[] { typeof(string), typeof(int) });

        var result = SagaEndpoints.GetRegisteredTypes(_resolver);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, statusResult.StatusCode);
    }

    [Fact]
    public void GetRegisteredTypes_should_return_empty_when_no_types_registered()
    {
        _resolver.GetRegisteredMessageTypes()
            .Returns(Enumerable.Empty<Type>());

        var result = SagaEndpoints.GetRegisteredTypes(_resolver);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, statusResult.StatusCode);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 101)]
    public async Task GetAll_should_return_BadRequest_for_invalid_pagination(int page, int pageSize)
    {
        var result = await SagaEndpoints.GetAll(_query, sagaType: null, isCompleted: null, page: page, pageSize: pageSize);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetByCorrelationId_should_return_BadRequest_when_sagaType_is_missing(string? sagaType)
    {
        var result = await SagaEndpoints.GetByCorrelationId(_query, "corr-1", sagaType!);

        Assert.IsType<BadRequest<string>>(result);
    }

    private static SagaInstanceInfo CreateSagaInstanceInfo(
        string instanceId,
        string correlationId = "corr-default",
        string triggerMessageId = "msg-default",
        string sagaType = "MySaga")
        => new()
        {
            InstanceId = instanceId,
            CorrelationId = correlationId,
            TriggerMessageId = triggerMessageId,
            SagaType = sagaType
        };
}
