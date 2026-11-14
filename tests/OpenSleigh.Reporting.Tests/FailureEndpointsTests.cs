using Microsoft.AspNetCore.Http.HttpResults;
using OpenSleigh.Queries;
using OpenSleigh.Reporting.Endpoints;

namespace OpenSleigh.Reporting.Tests;

public class FailureEndpointsTests
{
    private readonly ISagaFailureQuery _query = Substitute.For<ISagaFailureQuery>();

    public FailureEndpointsTests()
    {
        _query.GetAllAsync(Arg.Any<SagaFailureQueryFilter>(), Arg.Any<CancellationToken>())
              .Returns(call =>
              {
                  var filter = call.Arg<SagaFailureQueryFilter>();
                  return new PagedResult<SagaMessageFailure>([CreateFailure()], TotalCount: 1, filter.Page, filter.PageSize);
              });
    }

    private static SagaMessageFailure CreateFailure()
        => new()
        {
            Id = "failure-1",
            InstanceId = "instance-1",
            CorrelationId = "correlation-1",
            SagaType = "MySaga",
            MessageId = "message-1",
            MessageType = "MyMessage",
            Attempt = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            ExceptionType = typeof(InvalidOperationException).FullName!,
            ExceptionMessage = "whoops",
            Decision = FailureDecision.DeadLettered
        };

    [Fact]
    public async Task GetAll_should_return_the_page_the_query_returns()
    {
        var result = await FailureEndpoints.GetAll(_query, correlationId: null, sagaType: null, messageType: null, decision: null);

        var ok = Assert.IsType<Ok<PagedResult<SagaMessageFailure>>>(result);
        Assert.Equal("failure-1", Assert.Single(ok.Value!.Items).Id);
    }

    [Fact]
    public async Task GetAll_should_pass_its_parameters_to_the_query()
    {
        await FailureEndpoints.GetAll(
            _query, correlationId: "correlation-1", sagaType: "MySaga", messageType: "MyMessage",
            decision: FailureDecision.Compensated, page: 2, pageSize: 10);

        await _query.Received(1).GetAllAsync(
            Arg.Is<SagaFailureQueryFilter>(f =>
                f.InstanceId == null &&
                f.CorrelationId == "correlation-1" &&
                f.SagaType == "MySaga" &&
                f.MessageType == "MyMessage" &&
                f.Decision == FailureDecision.Compensated &&
                f.Page == 2 &&
                f.PageSize == 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByInstanceId_should_ask_for_the_failures_of_the_instance()
    {
        var result = await FailureEndpoints.GetByInstanceId(_query, "instance-1", page: 3, pageSize: 5);

        Assert.IsType<Ok<PagedResult<SagaMessageFailure>>>(result);
        await _query.Received(1).GetAllAsync(
            Arg.Is<SagaFailureQueryFilter>(f => f.InstanceId == "instance-1" && f.Page == 3 && f.PageSize == 5),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Endpoints_should_refuse_invalid_pagination(int page, int pageSize)
    {
        var all = await FailureEndpoints.GetAll(_query, null, null, null, null, page, pageSize);
        var byInstance = await FailureEndpoints.GetByInstanceId(_query, "instance-1", page, pageSize);

        Assert.IsType<BadRequest<string>>(all);
        Assert.IsType<BadRequest<string>>(byInstance);
        await _query.DidNotReceiveWithAnyArgs().GetAllAsync(default, default);
    }
}
