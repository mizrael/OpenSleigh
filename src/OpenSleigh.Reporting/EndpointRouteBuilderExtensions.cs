using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using OpenSleigh.Reporting.Endpoints;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("OpenSleigh.Reporting.Tests")]

namespace OpenSleigh.Reporting;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapOpenSleighReporting(this IEndpointRouteBuilder endpoints, string prefix = "/opensleigh")
    {
        var group = endpoints.MapGroup(prefix);

        group.MapGet("/sagas", SagaEndpoints.GetAll)
            .WithName("GetAllSagas");

        group.MapGet("/sagas/{instanceId}", SagaEndpoints.GetByInstanceId)
            .WithName("GetSagaByInstanceId");

        group.MapGet("/sagas/correlation/{correlationId}", SagaEndpoints.GetByCorrelationId)
            .WithName("GetSagaByCorrelationId");

        group.MapGet("/sagas/types", SagaEndpoints.GetRegisteredTypes)
            .WithName("GetRegisteredSagaTypes");

        return endpoints;
    }
}
