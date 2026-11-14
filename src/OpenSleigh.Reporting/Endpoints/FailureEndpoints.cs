using Microsoft.AspNetCore.Http;
using OpenSleigh.Queries;

namespace OpenSleigh.Reporting.Endpoints;

internal static class FailureEndpoints
{
    public static Task<IResult> GetAll(
        ISagaFailureQuery query,
        string? correlationId,
        string? sagaType,
        string? messageType,
        FailureDecision? decision,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
        => GetPage(
            query,
            new SagaFailureQueryFilter
            {
                CorrelationId = correlationId,
                SagaType = sagaType,
                MessageType = messageType,
                Decision = decision,
                Page = page,
                PageSize = pageSize
            },
            cancellationToken);

    public static Task<IResult> GetByInstanceId(
        ISagaFailureQuery query,
        string instanceId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
        => GetPage(
            query,
            new SagaFailureQueryFilter { InstanceId = instanceId, Page = page, PageSize = pageSize },
            cancellationToken);

    private static async Task<IResult> GetPage(ISagaFailureQuery query, SagaFailureQueryFilter filter, CancellationToken cancellationToken)
    {
        if (!Pagination.IsValid(filter.Page, filter.PageSize))
            return Results.BadRequest(Pagination.InvalidMessage);

        var result = await query.GetAllAsync(filter, cancellationToken);
        return Results.Ok(result);
    }
}
