using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

public class SagaDbContextRegistrationTests
{
    [Fact]
    public void Context_should_have_one_public_constructor_taking_only_its_options()
    {
        var constructor = Assert.Single(typeof(SagaDbContext).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(DbContextOptions<SagaDbContext>), parameter.ParameterType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddDbContext_should_resolve_the_context(bool withSchemaInitializer)
    {
        var services = CreateServices(withSchemaInitializer)
            .AddDbContext<SagaDbContext>(UseInMemoryDatabase, contextLifetime: ServiceLifetime.Transient);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SagaDbContext>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddDbContextPool_should_resolve_the_context(bool withSchemaInitializer)
    {
        var services = CreateServices(withSchemaInitializer)
            .AddDbContextPool<SagaDbContext>(UseInMemoryDatabase);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SagaDbContext>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddDbContextFactory_should_create_the_context(bool withSchemaInitializer)
    {
        var services = CreateServices(withSchemaInitializer)
            .AddDbContextFactory<SagaDbContext>(UseInMemoryDatabase);
        using var provider = services.BuildServiceProvider();

        using var context = provider.GetRequiredService<IDbContextFactory<SagaDbContext>>().CreateDbContext();

        Assert.NotNull(context);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddPooledDbContextFactory_should_create_the_context(bool withSchemaInitializer)
    {
        var services = CreateServices(withSchemaInitializer)
            .AddPooledDbContextFactory<SagaDbContext>(UseInMemoryDatabase);
        using var provider = services.BuildServiceProvider();

        using var context = provider.GetRequiredService<IDbContextFactory<SagaDbContext>>().CreateDbContext();

        Assert.NotNull(context);
    }

    private static IServiceCollection CreateServices(bool withSchemaInitializer)
    {
        var services = new ServiceCollection();
        if (withSchemaInitializer)
            services.AddSingleton<SagaDbSchemaInitializer>();
        return services;
    }

    private static void UseInMemoryDatabase(DbContextOptionsBuilder builder)
        => builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
}
