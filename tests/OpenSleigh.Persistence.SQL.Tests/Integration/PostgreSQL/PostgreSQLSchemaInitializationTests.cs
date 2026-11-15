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
    private readonly PostgreSQLDbFixture _fixture;

    public PostgreSQLSchemaInitializationTests(PostgreSQLDbFixture fixture) : base(fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lock_that_cannot_be_released_should_not_go_back_to_the_pool_with_its_connection(bool useDataSource)
    {
        const string waitForTheLock = "SET lock_timeout = '10s'; " + PostgreSqlSchemaUpgrade.LockForConnection;
        var (existing, connectionString) = _fixture.CreateDbContext();
        await existing.Database.EnsureCreatedAsync();

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var options = new DbContextOptionsBuilder<SagaDbContext>();
        await using var context = new SagaDbContext(
            (useDataSource ? options.UseNpgsql(dataSource) : options.UseNpgsql(connectionString)).Options);

        await context.Database.OpenConnectionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync(PostgreSqlSchemaUpgrade.LockForConnection);

            // a transaction that failed refuses every statement, the one that lets go of the lock included
            await context.Database.ExecuteSqlRawAsync("BEGIN");
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("SELECT 1/0"));

            PostgreSqlSchemaUpgrade.ReleaseLockOrConnection(context.Database);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        // another instance: it fails if the lock is still held by a connection that waits in a pool
        var otherInstance = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(otherInstance.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = waitForTheLock;
        await command.ExecuteNonQueryAsync();
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
