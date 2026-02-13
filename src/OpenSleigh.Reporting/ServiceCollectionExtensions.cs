using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.Reporting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenSleighReporting(this IServiceCollection services)
    {
        return services;
    }
}
