using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

internal static class ServiceDecoration
{
    /// <summary>
    /// Replaces the last registration of <typeparamref name="TService"/> with one that wraps it, keeping its lifetime.
    /// Returns false when the service is not registered.
    /// </summary>
    public static bool TryDecorate<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService) && !d.IsKeyedService);
        if (descriptor is null)
            return false;

        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(TService),
            provider => decorate(provider, CreateInner<TService>(provider, descriptor)),
            descriptor.Lifetime));
        return true;
    }

    private static TService CreateInner<TService>(IServiceProvider provider, ServiceDescriptor descriptor)
        where TService : class
    {
        if (descriptor.ImplementationInstance is TService instance)
            return instance;

        if (descriptor.ImplementationFactory is not null)
            return (TService)descriptor.ImplementationFactory(provider);

        return (TService)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}
