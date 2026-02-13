using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.Reporting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenSleighReporting(this IServiceCollection services)
    {
#if NET9_0_OR_GREATER
        services.AddOpenApi();
#endif
        return services;
    }
}
