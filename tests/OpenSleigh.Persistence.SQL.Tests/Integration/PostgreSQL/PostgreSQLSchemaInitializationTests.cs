using Npgsql;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using System.Linq;

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

    protected override string ReleasedSchemaScriptFileName => "v3.3.0.postgres.sql";

    protected override string OutboxIndexNamesQuery =>
        "SELECT indexname::text AS \"Value\" FROM pg_indexes " +
        $"WHERE schemaname = '{Constants.DbSchema}' AND tablename = '{Entities.OutboxMessage.TableName}'";

    protected override async Task<string> CreateUserWithoutSchemaPermissionsAsync(SagaDbContext admin, string connectionString)
    {
        const string password = "Sup3r_p4ssword123";
        var user = "osl_" + Guid.NewGuid().ToString("N");

        await admin.Database.ExecuteSqlRawAsync("CREATE ROLE " + user + " LOGIN PASSWORD '" + password + "'");
        await admin.Database.ExecuteSqlRawAsync("GRANT USAGE ON SCHEMA " + Constants.DbSchema + " TO " + user);
        await admin.Database.ExecuteSqlRawAsync(
            "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA " + Constants.DbSchema + " TO " + user);

        return new NpgsqlConnectionStringBuilder(connectionString) { Username = user, Password = password }.ConnectionString;
    }

    protected override async Task UpgradeWhileOthersWaitAsync(SagaDbContext admin, Func<Task> whileOthersWait)
    {
        // the lock belongs to the connection: it is kept open until the lock is let go
        await admin.Database.OpenConnectionAsync();
        try
        {
            await admin.Database.ExecuteSqlRawAsync(PostgreSqlSchemaUpgrade.LockForConnection);
            await whileOthersWait();

            foreach (var statement in new PostgreSqlSchemaUpgrade().Changes.SelectMany(change => change.Statements))
                await admin.Database.ExecuteSqlRawAsync(statement);

            await admin.Database.ExecuteSqlRawAsync(PostgreSqlSchemaUpgrade.ReleaseLockForConnection);
        }
        finally
        {
            await admin.Database.CloseConnectionAsync();
        }
    }
}
