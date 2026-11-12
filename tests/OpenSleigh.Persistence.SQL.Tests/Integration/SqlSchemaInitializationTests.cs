using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using System.ComponentModel;

namespace OpenSleigh.Persistence.SQL.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public abstract class SqlSchemaInitializationTests
{
    private readonly DbFixture _fixture;

    protected SqlSchemaInitializationTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    protected abstract void UsePersistence(IBusConfigurator configurator, SqlConfiguration configuration);

    [Fact]
    public async Task First_context_of_a_service_provider_should_create_the_database_and_its_schema()
    {
        var connectionString = await CreateMissingDatabaseAsync();

        await using var provider = BuildProvider(connectionString);

        Assert.False(await HasSagasAsync(provider));
    }

    [Fact]
    public async Task Schema_should_be_checked_once_per_service_provider()
    {
        var connectionString = await CreateMissingDatabaseAsync();

        await using (var provider = BuildProvider(connectionString))
        {
            Assert.False(await HasSagasAsync(provider));
            await DropDatabaseAsync(provider);

            // the same provider does not look again: this is what saves a round trip per context
            await Assert.ThrowsAnyAsync<Exception>(() => HasSagasAsync(provider));
        }

        // a new provider in the same process does, even for a database it has seen before
        await using var newProvider = BuildProvider(connectionString);
        Assert.False(await HasSagasAsync(newProvider));
    }

    private async Task<string> CreateMissingDatabaseAsync()
    {
        // the fixture creates the database and will remove it at the end: it is dropped here to start without one
        var (db, connectionString) = _fixture.CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        return connectionString;
    }

    private ServiceProvider BuildProvider(string connectionString)
        => new ServiceCollection()
            .AddOpenSleigh(configurator => UsePersistence(configurator, new SqlConfiguration(connectionString)))
            .BuildServiceProvider();

    private static async Task<bool> HasSagasAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        return await db.SagaStates.AnyAsync();
    }

    private static async Task DropDatabaseAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        await db.Database.EnsureDeletedAsync();
    }
}
