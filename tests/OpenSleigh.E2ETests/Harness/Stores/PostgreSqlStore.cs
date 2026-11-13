using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Persistence.SQL;

namespace OpenSleigh.E2ETests.Harness.Stores;

internal sealed class PostgreSqlStore(RunContext context) : SqlStore(context, ConnectionStringName)
{
    private const string ConnectionStringName = "postgre";

    protected override void UsePersistence(IBusConfigurator bus, SqlConfiguration configuration)
        => bus.UsePostgreSqlPersistence(configuration);
}
