using Microsoft.Data.SqlClient;
using Npgsql;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// takes the database of one scenario run away from its hosts, with what the server itself offers:
/// the other runs, which have databases of their own on the same server, notice nothing.
/// Disposing it ends an outage that is still going on, so that the run can remove its database.
/// </summary>
public abstract class DatabaseOutage : IAsyncDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private bool _isOn;

    /// <exception cref="NotSupportedException">the store of the run has no way to be taken away for one database only.</exception>
    public static DatabaseOutage Of(ScenarioRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        // the same settings, and the same database name, as the store of the run
        var context = RunContext.Create() with { RunId = run.RunId };
        string ConnectionString(string name) => string.Format(context.GetRequiredConnectionString(name), context.RunId);

        if (Combinations.Uses(run.Combination.Name, Combinations.PostgreSql))
            return new PostgreSql(ConnectionString(PostgreSql.ConnectionStringName));
        if (Combinations.Uses(run.Combination.Name, Combinations.SqlServer))
            return new SqlServer(ConnectionString(SqlServer.ConnectionStringName));

        throw new NotSupportedException($"the database of '{run.Combination.Name}' cannot be taken away on its own.");
    }

    /// <summary>
    /// closes every connection to the database, as a network that drops them would. The database can be connected to
    /// again right away: what the hosts find broken is what they had open.
    /// </summary>
    public abstract Task DropConnectionsAsync(CancellationToken cancellationToken);

    /// <summary>closes every connection to the database and refuses new ones, until <see cref="EndAsync"/>.</summary>
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        // marked first: an outage that began and could not be told so is ended all the same
        _isOn = true;
        await BeginCoreAsync(cancellationToken);
    }

    public async Task EndAsync(CancellationToken cancellationToken)
    {
        await EndCoreAsync(cancellationToken);
        _isOn = false;
    }

    protected abstract Task BeginCoreAsync(CancellationToken cancellationToken);

    protected abstract Task EndCoreAsync(CancellationToken cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (!_isOn)
            return;

        using var timeout = new CancellationTokenSource(CommandTimeout);
        await EndAsync(timeout.Token);
    }

    private sealed class PostgreSql : DatabaseOutage
    {
        public const string ConnectionStringName = "postgre";
        private const string MaintenanceDatabase = "postgres";

        private readonly string _serverConnectionString;
        private readonly string _database;

        public PostgreSql(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            _database = builder.Database ?? throw new InvalidOperationException("the connection string names no database.");

            // not pooled, and not to the database that is taken away
            builder.Database = MaintenanceDatabase;
            builder.Pooling = false;
            _serverConnectionString = builder.ConnectionString;
        }

        public override Task DropConnectionsAsync(CancellationToken cancellationToken)
            => ExecuteAsync(TerminateSessions, cancellationToken);

        protected override Task BeginCoreAsync(CancellationToken cancellationToken)
            => ExecuteAsync($"{AllowConnections(false)}; {TerminateSessions}", cancellationToken);

        protected override Task EndCoreAsync(CancellationToken cancellationToken)
            => ExecuteAsync(AllowConnections(true), cancellationToken);

        private string AllowConnections(bool allowed)
            => $"ALTER DATABASE \"{_database}\" WITH ALLOW_CONNECTIONS {(allowed ? "true" : "false")}";

        private string TerminateSessions
            => $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_database}' AND pid <> pg_backend_pid()";

        private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_serverConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = (int)CommandTimeout.TotalSeconds };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed class SqlServer : DatabaseOutage
    {
        public const string ConnectionStringName = "sql";
        private const string MaintenanceDatabase = "master";

        private readonly string _serverConnectionString;
        private readonly string _database;

        public SqlServer(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            _database = builder.InitialCatalog;

            // not pooled, and not to the database that is taken away
            builder.InitialCatalog = MaintenanceDatabase;
            builder.Pooling = false;
            _serverConnectionString = builder.ConnectionString;
        }

        // a database that goes offline rolls back what is running and closes its connections
        public override Task DropConnectionsAsync(CancellationToken cancellationToken)
            => ExecuteAsync($"{SetOffline}; {SetOnline}", cancellationToken);

        protected override Task BeginCoreAsync(CancellationToken cancellationToken)
            => ExecuteAsync(SetOffline, cancellationToken);

        protected override Task EndCoreAsync(CancellationToken cancellationToken)
            => ExecuteAsync(SetOnline, cancellationToken);

        private string SetOffline => $"ALTER DATABASE [{_database}] SET OFFLINE WITH ROLLBACK IMMEDIATE";

        private string SetOnline => $"ALTER DATABASE [{_database}] SET ONLINE";

        private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(_serverConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection) { CommandTimeout = (int)CommandTimeout.TotalSeconds };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
