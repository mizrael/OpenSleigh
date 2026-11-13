using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQLServer;

namespace OpenSleigh.E2ETests.Harness.Stores;

internal sealed class SqlServerStore(RunContext context) : SqlStore(context, ConnectionStringName)
{
    private const string ConnectionStringName = "sql";

    protected override void UsePersistence(IBusConfigurator bus, SqlConfiguration configuration)
        => bus.UseSqlServerPersistence(configuration);
}
