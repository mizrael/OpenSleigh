using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// takes the database of one scenario run away from its hosts, with what the server itself offers:
/// the other runs, which have databases of their own on the same server, notice nothing.
/// Disposing it ends an outage that is still going on, so that the run can remove its database.
/// </summary>
public abstract class DatabaseOutage : IAsyncDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _console;
    private bool _isOn;

    /// <param name="database">the database that is taken away. It has to be the one of the run and no other.</param>
    protected DatabaseOutage(string? database, string runId, ITestOutputHelper console)
    {
        if (string.IsNullOrWhiteSpace(database) || !database.Contains(runId, StringComparison.Ordinal))
            throw new InvalidOperationException($"'{database}' is not the database of run '{runId}': it is left alone.");

        Database = database;
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    protected string Database { get; }

    /// <param name="console">where a clean-up that fails is reported.</param>
    /// <exception cref="NotSupportedException">the store of the run has no way to be taken away for one database only.</exception>
    public static DatabaseOutage Of(ScenarioRun run, ITestOutputHelper console)
    {
        ArgumentNullException.ThrowIfNull(run);

        // the same settings, and the same database name, as the store of the run
        var context = RunContext.Create() with { RunId = run.RunId };
        string ConnectionString(string name) => string.Format(context.GetRequiredConnectionString(name), context.RunId);

        if (Combinations.Uses(run.Combination.Name, Combinations.PostgreSql))
            return new PostgreSql(ConnectionString(PostgreSql.ConnectionStringName), run.RunId, console);
        if (Combinations.Uses(run.Combination.Name, Combinations.SqlServer))
            return new SqlServer(ConnectionString(SqlServer.ConnectionStringName), run.RunId, console);

        throw new NotSupportedException($"the database of '{run.Combination.Name}' cannot be taken away on its own.");
    }

    /// <summary>
    /// closes every connection to the database, as a network that drops them would. The database stays available:
    /// what the hosts find broken is what they had open.
    /// </summary>
    /// <returns>how many connections were closed. None when nobody had one open: nothing was interrupted.</returns>
    public abstract Task<long> DropConnectionsAsync(CancellationToken cancellationToken);

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

    // what goes wrong here is written to the output, as the run does for its own clean-up:
    // it never replaces what the scenario failed with
    public async ValueTask DisposeAsync()
    {
        if (!_isOn)
            return;

        try
        {
            using var timeout = new CancellationTokenSource(CommandTimeout);
            await EndAsync(timeout.Token);
        }
        catch (Exception e)
        {
            _console.WriteLine($"{ScenarioRun.CleanUpFailed}: the outage of database '{Database}' could not be ended. {e}");
        }
    }

    private sealed class PostgreSql : DatabaseOutage
    {
        public const string ConnectionStringName = "postgre";
        private const string MaintenanceDatabase = "postgres";
        private const string DatabaseParameter = "database";
        private const string SessionsParameter = "sessions";
        private static readonly TimeSpan SessionsPollingInterval = TimeSpan.FromMilliseconds(20);

        // asked in the list of what is returned, which is only worked out for the sessions of the database:
        // in the condition it could be asked of any session of the server
        private const string TerminateSessions =
            "SELECT pid, pg_terminate_backend(pid) FROM pg_stat_activity " +
            $"WHERE datname = @{DatabaseParameter} AND pid <> pg_backend_pid()";

        private const string CountSessions = $"SELECT count(*) FROM pg_stat_activity WHERE datname = @{DatabaseParameter}";

        private const string CountSessionsAmong = $"SELECT count(*) FROM pg_stat_activity WHERE pid = ANY(@{SessionsParameter})";

        private readonly string _serverConnectionString;

        public PostgreSql(string connectionString, string runId, ITestOutputHelper console)
            : base(new NpgsqlConnectionStringBuilder(connectionString).Database, runId, console)
        {
            // not pooled, and not to the database that is taken away
            _serverConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = MaintenanceDatabase,
                Pooling = false,
            }.ConnectionString;
        }

        // a session is asked to end and ends a moment later: this returns once the ones that were asked are gone
        public override async Task<long> DropConnectionsAsync(CancellationToken cancellationToken)
        {
            var terminated = await TerminateSessionsAsync(cancellationToken);
            if (terminated.Length == 0)
                return 0;

            while (await CountAsync(CountSessionsAmong, new NpgsqlParameter(SessionsParameter, terminated), cancellationToken) > 0)
                await Task.Delay(SessionsPollingInterval, cancellationToken);

            return terminated.Length;
        }

        // the refusal is committed before the sessions are closed: sent together they would be one transaction,
        // and a host that connected while it ran would keep its session. A session that was being opened
        // when the others were closed shows up afterwards: they are closed until none is left
        protected override async Task BeginCoreAsync(CancellationToken cancellationToken)
        {
            await AllowConnectionsAsync(false, cancellationToken);

            do
            {
                await TerminateSessionsAsync(cancellationToken);
                await Task.Delay(SessionsPollingInterval, cancellationToken);
            } while (await CountAsync(CountSessions, new NpgsqlParameter(DatabaseParameter, Database), cancellationToken) > 0);
        }

        protected override Task EndCoreAsync(CancellationToken cancellationToken)
            => AllowConnectionsAsync(true, cancellationToken);

        // the name of a database cannot be a parameter of this statement: it is the one the constructor checked
        private async Task AllowConnectionsAsync(bool allowed, CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = CreateCommand(
                connection, $"ALTER DATABASE \"{Database}\" WITH ALLOW_CONNECTIONS {(allowed ? "true" : "false")}");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<int[]> TerminateSessionsAsync(CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = CreateCommand(connection, TerminateSessions, new NpgsqlParameter(DatabaseParameter, Database));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            // only the sessions the server agreed to terminate
            var sessions = new List<int>();
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetBoolean(1))
                    sessions.Add(reader.GetInt32(0));
            }

            return sessions.ToArray();
        }

        private async Task<long> CountAsync(string sql, NpgsqlParameter parameter, CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = CreateCommand(connection, sql, parameter);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
        {
            var connection = new NpgsqlConnection(_serverConnectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql, params NpgsqlParameter[] parameters)
        {
            var command = new NpgsqlCommand(sql, connection) { CommandTimeout = (int)CommandTimeout.TotalSeconds };
            command.Parameters.AddRange(parameters);
            return command;
        }
    }

    private sealed class SqlServer : DatabaseOutage
    {
        public const string ConnectionStringName = "sql";
        private const string MaintenanceDatabase = "master";
        private const string DatabaseParameter = "@database";

        // the sessions of the database are killed one by one, as the PostgreSQL side terminates backends,
        // and the database stays online. One that ended in the meantime is not there to be killed, and is not counted
        private const string KillSessions =
            "DECLARE @killed int = 0, @session int, @kill nvarchar(32); " +
            "DECLARE sessions CURSOR LOCAL FAST_FORWARD FOR " +
            $"SELECT session_id FROM sys.dm_exec_sessions WHERE database_id = DB_ID({DatabaseParameter}) AND session_id <> @@SPID; " +
            "OPEN sessions; " +
            "FETCH NEXT FROM sessions INTO @session; " +
            "WHILE @@FETCH_STATUS = 0 " +
            "BEGIN " +
            "SET @kill = N'KILL ' + CAST(@session AS nvarchar(10)); " +
            "BEGIN TRY EXEC (@kill); SET @killed += 1; END TRY BEGIN CATCH END CATCH; " +
            "FETCH NEXT FROM sessions INTO @session; " +
            "END; " +
            "CLOSE sessions; DEALLOCATE sessions; " +
            "SELECT @killed;";

        private readonly string _serverConnectionString;

        public SqlServer(string connectionString, string runId, ITestOutputHelper console)
            : base(new SqlConnectionStringBuilder(connectionString).InitialCatalog, runId, console)
        {
            // not pooled, and not to the database that is taken away
            _serverConnectionString = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = MaintenanceDatabase,
                Pooling = false,
            }.ConnectionString;
        }

        public override async Task<long> DropConnectionsAsync(CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = CreateCommand(connection, KillSessions);
            command.Parameters.AddWithValue(DatabaseParameter, Database);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        // a database that goes offline rolls back what is running and closes its connections.
        // Its name cannot be a parameter of these statements: it is the one the constructor checked
        protected override Task BeginCoreAsync(CancellationToken cancellationToken)
            => ExecuteAsync($"ALTER DATABASE [{Database}] SET OFFLINE WITH ROLLBACK IMMEDIATE", cancellationToken);

        protected override Task EndCoreAsync(CancellationToken cancellationToken)
            => ExecuteAsync($"ALTER DATABASE [{Database}] SET ONLINE", cancellationToken);

        private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = CreateCommand(connection, sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
        {
            var connection = new SqlConnection(_serverConnectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private static SqlCommand CreateCommand(SqlConnection connection, string sql)
            => new(sql, connection) { CommandTimeout = (int)CommandTimeout.TotalSeconds };
    }
}
