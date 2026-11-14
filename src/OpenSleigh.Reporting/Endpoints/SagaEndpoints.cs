using Microsoft.AspNetCore.Http;
using OpenSleigh.Queries;

namespace OpenSleigh.Reporting.Endpoints;

internal static class SagaEndpoints
{
    public static async Task<IResult> GetAll(
        ISagaStateQuery query,
        string? sagaType,
        bool? isCompleted,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!Pagination.IsValid(page, pageSize))
            return Results.BadRequest(Pagination.InvalidMessage);

        var filter = new SagaQueryFilter
        {
            SagaType = sagaType,
            IsCompleted = isCompleted,
            Page = page,
            PageSize = pageSize
        };

        var result = await query.GetAllAsync(filter, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetByInstanceId(
        ISagaStateQuery query,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var result = await query.GetByInstanceIdAsync(instanceId, cancellationToken);
        return result is not null
            ? Results.Ok(result)
            : Results.NotFound();
    }

    public static async Task<IResult> GetByCorrelationId(
        ISagaStateQuery query,
        string correlationId,
        string sagaType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sagaType))
            return Results.BadRequest("sagaType is required.");

        var result = await query.GetByCorrelationIdAsync(correlationId, sagaType, cancellationToken);
        return result is not null
            ? Results.Ok(result)
            : Results.NotFound();
    }

    public static IResult GetRegisteredTypes(ISagaDescriptorsResolver resolver)
    {
        var messageTypes = resolver.GetRegisteredMessageTypes();

        var result = messageTypes.Select(mt => new
        {
            MessageType = mt.FullName
        });

        return Results.Ok(result);
    }
}
