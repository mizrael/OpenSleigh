using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenSleigh.Queries;
using OpenSleigh.Reporting.Endpoints;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("OpenSleigh.Reporting.Tests")]

namespace OpenSleigh.Reporting;

public static class EndpointRouteBuilderExtensions
{
    private const string Tag = "OpenSleigh";

    /// <summary>
    /// Maps the OpenSleigh reporting endpoints and (on .NET 9+) the OpenAPI document.
    /// <para>
    /// <b>Security note:</b> these endpoints expose saga state data, which may contain
    /// sensitive information. Ensure appropriate authentication and authorization
    /// middleware is configured before calling this method in production environments.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapOpenSleighReporting(this IEndpointRouteBuilder endpoints, string prefix = "/opensleigh")
    {
#if NET9_0_OR_GREATER
        endpoints.MapOpenApi();
#endif

        var group = endpoints.MapGroup(prefix)
            .WithTags(Tag);

        group.MapGet("/sagas", SagaEndpoints.GetAll)
            .WithName("GetAllSagas")
            .WithSummary("List saga instances")
            .WithDescription("Returns a paginated list of saga instances, optionally filtered by saga type and completion status.")
            .Produces<PagedResult<SagaInstanceInfo>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/sagas/{instanceId}", SagaEndpoints.GetByInstanceId)
            .WithName("GetSagaByInstanceId")
            .WithSummary("Get a saga instance by ID")
            .WithDescription("Returns the full saga instance details including state data and processed messages.")
            .Produces<SagaInstanceInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/sagas/correlation/{correlationId}", SagaEndpoints.GetByCorrelationId)
            .WithName("GetSagaByCorrelationId")
            .WithSummary("Get a saga instance by correlation ID")
            .WithDescription("Looks up a saga instance by its correlation ID and saga type.")
            .Produces<SagaInstanceInfo>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/sagas/types", SagaEndpoints.GetRegisteredTypes)
            .WithName("GetRegisteredSagaTypes")
            .WithSummary("List registered message types")
            .WithDescription("Returns the message types that have registered saga handlers.");

        return endpoints;
    }
}
