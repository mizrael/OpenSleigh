using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Persistence.SQLServer;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.SQLServer;

public class SqlServerSchemaInitializationTests :
    SqlSchemaInitializationTests,
    IClassFixture<SqlServerDbFixture>
{
    public SqlServerSchemaInitializationTests(SqlServerDbFixture fixture) : base(fixture)
    {
    }

    protected override void UsePersistence(IBusConfigurator configurator, SqlConfiguration configuration)
        => configurator.UseSqlServerPersistence(configuration);
}
