using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.PostgreSQL;

public class PostgreSQLSchemaInitializationTests :
    SqlSchemaInitializationTests,
    IClassFixture<PostgreSQLDbFixture>
{
    public PostgreSQLSchemaInitializationTests(PostgreSQLDbFixture fixture) : base(fixture)
    {
    }

    protected override void UsePersistence(IBusConfigurator configurator, SqlConfiguration configuration)
        => configurator.UsePostgreSqlPersistence(configuration);
}
