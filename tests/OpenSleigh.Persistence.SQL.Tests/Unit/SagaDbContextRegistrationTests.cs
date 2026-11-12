using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

public class SagaDbContextRegistrationTests
{
    [Fact]
    public void Context_should_resolve_when_the_schema_initializer_is_registered()
    {
        using var provider = CreateServices()
            .AddSingleton<SagaDbSchemaInitializer>()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SagaDbContext>());
    }

    [Fact]
    public void Context_should_resolve_when_the_schema_initializer_is_not_registered()
    {
        using var provider = CreateServices().BuildServiceProvider();

        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SagaDbContext>());
    }

    [Fact]
    public void Constructor_should_throw_when_the_schema_initializer_is_null()
    {
        var options = new DbContextOptionsBuilder<SagaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        Assert.Throws<ArgumentNullException>(() => new SagaDbContext(options, null!));
    }

    private static IServiceCollection CreateServices()
        => new ServiceCollection()
            .AddDbContext<SagaDbContext>(
                builder => builder.UseInMemoryDatabase(Guid.NewGuid().ToString()),
                contextLifetime: ServiceLifetime.Transient);
}
