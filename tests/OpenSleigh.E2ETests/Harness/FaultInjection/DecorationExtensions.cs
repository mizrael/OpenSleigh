using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// replaces a service of a host with one that wraps it, to make it fail, wait or count on purpose.
/// Call it from <see cref="HostSetup.ConfigureServices"/>, which runs once the library has registered its services.
/// Decorators shared by several scenarios belong in this folder.
/// </summary>
public static class DecorationExtensions
{
    /// <param name="decorate">builds the replacement from the services of the host and the service it replaces.</param>
    /// <exception cref="InvalidOperationException">the service is not registered.</exception>
    public static IServiceCollection Decorate<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(decorate);

        // the last registration is the one the container resolves
        var index = services.ToList().FindLastIndex(descriptor => descriptor.ServiceType == typeof(TService));
        if (index < 0)
            throw new InvalidOperationException($"'{typeof(TService).FullName}' is not registered: there is nothing to decorate.");

        var inner = services[index];
        services[index] = ServiceDescriptor.Describe(
            typeof(TService),
            provider => decorate(provider, (TService)CreateInner(provider, inner)),
            inner.Lifetime);

        return services;
    }

    private static object CreateInner(IServiceProvider provider, ServiceDescriptor inner)
    {
        if (inner.ImplementationInstance is not null)
            return inner.ImplementationInstance;

        if (inner.ImplementationFactory is not null)
            return inner.ImplementationFactory(provider);

        return ActivatorUtilities.CreateInstance(provider, inner.ImplementationType!);
    }
}
