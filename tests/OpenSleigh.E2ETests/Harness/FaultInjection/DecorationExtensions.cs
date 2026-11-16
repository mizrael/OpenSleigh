using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Outbox;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// replaces a service of a host with one that wraps it, to make it fail, wait or count on purpose.
/// Call it from <see cref="HostSetup.ConfigureServices"/>, which runs once the library has registered its services.
/// Decorators shared by several scenarios belong in this folder.
/// </summary>
public static class DecorationExtensions
{
    // what the library looks for on a service besides the interface it asked for. A wrapper that implements
    // only that interface would hide them, and the library would quietly take another path
    private static readonly IReadOnlyDictionary<Type, Type[]> OptionalInterfaces = new Dictionary<Type, Type[]>
    {
        [typeof(ISagaStateRepository)] = [typeof(ISagaStateCommitter)],
        [typeof(IOutboxRepository)] = [typeof(IOutboxBatchRepository)],
    };

    /// <summary>
    /// replaces the last registration of a service. The service it wraps stays in the container,
    /// with its lifetime: it is created once per scope, or once at all, as before, and disposed with its owner.
    /// For <see cref="ISagaStateRepository"/> and <see cref="IOutboxRepository"/> use
    /// <see cref="Intercept(IServiceCollection, Func{IServiceProvider, SagaStateRepositoryInterceptor})"/> instead.
    /// </summary>
    /// <param name="decorate">builds the replacement from the services of the host and the service it replaces.</param>
    /// <exception cref="InvalidOperationException">the service is not registered; or, when it is resolved,
    /// the replacement lacks an interface of the original that the library looks for.</exception>
    public static IServiceCollection Decorate<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(decorate);

        // the last registration that has no key is the one the container resolves
        var index = services.ToList().FindLastIndex(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService);
        if (index < 0)
            throw new InvalidOperationException($"'{typeof(TService).FullName}' is not registered: there is nothing to decorate.");

        // the original moves under a key of its own, where only its decorator finds it
        var innerKey = new object();
        var inner = services[index];
        services[index] = WithKey(inner, innerKey);

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider =>
            {
                var original = provider.GetRequiredKeyedService<TService>(innerKey);
                return RequireOptionalInterfaces(original, decorate(provider, original));
            },
            inner.Lifetime));

        return services;
    }

    private static ServiceDescriptor WithKey(ServiceDescriptor descriptor, object key)
    {
        if (descriptor.ImplementationInstance is not null)
            return new ServiceDescriptor(descriptor.ServiceType, key, descriptor.ImplementationInstance);

        if (descriptor.ImplementationFactory is not null)
            return new ServiceDescriptor(descriptor.ServiceType, key, (provider, _) => descriptor.ImplementationFactory(provider), descriptor.Lifetime);

        return new ServiceDescriptor(descriptor.ServiceType, key, descriptor.ImplementationType!, descriptor.Lifetime);
    }

    private static TService RequireOptionalInterfaces<TService>(TService original, TService replacement)
        where TService : class
    {
        if (!OptionalInterfaces.TryGetValue(typeof(TService), out var optionalInterfaces))
            return replacement;

        var hidden = optionalInterfaces.FirstOrDefault(optional => optional.IsInstanceOfType(original) && !optional.IsInstanceOfType(replacement));
        if (hidden is not null)
            throw new InvalidOperationException(
                $"'{replacement.GetType().Name}' replaces a {typeof(TService).Name} that is also a {hidden.Name} and is not one itself: " +
                $"the library would stop using it as a {hidden.Name}. Use {nameof(Intercept)} instead of {nameof(Decorate)}.");

        return replacement;
    }

    /// <summary>
    /// puts an interceptor in front of the saga state repository of a host, whatever else that repository is.
    /// </summary>
    public static IServiceCollection Intercept(
        this IServiceCollection services,
        Func<IServiceProvider, SagaStateRepositoryInterceptor> createInterceptor)
    {
        ArgumentNullException.ThrowIfNull(createInterceptor);
        return services.Decorate<ISagaStateRepository>((provider, inner) => InterceptedSagaStateRepository.Create(inner, createInterceptor(provider)));
    }

    /// <summary>
    /// puts an interceptor in front of the outbox repository of a host, whatever else that repository is.
    /// </summary>
    public static IServiceCollection Intercept(
        this IServiceCollection services,
        Func<IServiceProvider, OutboxRepositoryInterceptor> createInterceptor)
    {
        ArgumentNullException.ThrowIfNull(createInterceptor);
        return services.Decorate<IOutboxRepository>((provider, inner) => InterceptedOutboxRepository.Create(inner, createInterceptor(provider)));
    }
}
