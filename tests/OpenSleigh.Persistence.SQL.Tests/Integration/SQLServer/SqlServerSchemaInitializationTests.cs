using Microsoft.Data.SqlClient;
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

    protected override string ReleasedSchemaScriptFileName => "v3.3.0.sqlserver.sql";

    protected override string OutboxIndexNamesQuery =>
        "SELECT CAST(name AS nvarchar(128)) AS [Value] FROM sys.indexes " +
        $"WHERE object_id = OBJECT_ID(N'{Constants.DbSchema}.{Entities.OutboxMessage.TableName}') AND name IS NOT NULL";

    protected override async Task<string> CreateUserWithoutSchemaPermissionsAsync(SagaDbContext admin, string connectionString)
    {
        const string password = "Sup3r_p4ssword123";
        var user = "osl_" + Guid.NewGuid().ToString("N");

        await admin.Database.ExecuteSqlRawAsync("CREATE LOGIN [" + user + "] WITH PASSWORD = '" + password + "', CHECK_POLICY = OFF");
        await admin.Database.ExecuteSqlRawAsync("CREATE USER [" + user + "] FOR LOGIN [" + user + "]");
        await admin.Database.ExecuteSqlRawAsync("ALTER ROLE db_datareader ADD MEMBER [" + user + "]");
        await admin.Database.ExecuteSqlRawAsync("ALTER ROLE db_datawriter ADD MEMBER [" + user + "]");

        return new SqlConnectionStringBuilder(connectionString) { UserID = user, Password = password }.ConnectionString;
    }
}
