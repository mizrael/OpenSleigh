using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OpenSleigh.Persistence.SQL;

public static class SagaDbContextServiceCollectionExtensions
{
    /// <summary>
    /// registers <see cref="SagaDbContext"/> as a transient service whose instances are leased from a pool:
    /// whoever asks for a context still gets one of its own, but the context is reused once it is disposed
    /// instead of being built, with everything EF Core sets up on its first use, for every message.
    /// </summary>
    public static IServiceCollection AddPooledSagaDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddPooledDbContextFactory<SagaDbContext>(configureOptions);

        // the factory registration may add the context itself with a scoped lifetime: every consumer gets its own here
        services.RemoveAll<SagaDbContext>();
        services.AddTransient(provider => provider.GetRequiredService<IDbContextFactory<SagaDbContext>>().CreateDbContext());

        return services;
    }
}
