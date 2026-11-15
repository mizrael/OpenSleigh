using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenSleigh.Persistence.SQL;

/// <summary>
/// creates the saga database and its tables when they are missing, the way EnsureCreated does,
/// for instances that start together: EnsureCreated checks and then creates, and all but one of them
/// would fail on what another one created in between.
/// </summary>
internal sealed class SagaDbCreation
{
    private static readonly TimeSpan DefaultDatabaseWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(250);

    private readonly IRelationalDatabaseCreator _creator;
    private readonly Func<Exception, bool> _isBeingCreatedByAnotherInstance;
    private readonly Action _forgetFailedConnections;
    private readonly Action<Action> _runDatabaseCreationExclusively;
    private readonly ILogger _logger;
    private readonly TimeSpan _databaseWait;

    /// <param name="isBeingCreatedByAnotherInstance">
    /// whether creating the database failed because another instance is creating it, or just has.
    /// </param>
    /// <param name="forgetFailedConnections">
    /// called before asking again whether the database exists: a provider can keep answering from a failure it remembers.
    /// </param>
    /// <param name="runDatabaseCreationExclusively">
    /// runs what it is given while no other instance creates the same database, when the server has a way to.
    /// </param>
    /// <param name="databaseWait">how long to wait for a database that another instance is creating.</param>
    public SagaDbCreation(
        IRelationalDatabaseCreator creator,
        Func<Exception, bool> isBeingCreatedByAnotherInstance,
        Action? forgetFailedConnections = null,
        Action<Action>? runDatabaseCreationExclusively = null,
        ILogger? logger = null,
        TimeSpan? databaseWait = null)
    {
        _creator = creator ?? throw new ArgumentNullException(nameof(creator));
        _isBeingCreatedByAnotherInstance = isBeingCreatedByAnotherInstance ?? throw new ArgumentNullException(nameof(isBeingCreatedByAnotherInstance));
        _forgetFailedConnections = forgetFailedConnections ?? (() => { });
        _runDatabaseCreationExclusively = runDatabaseCreationExclusively ?? (createDatabase => createDatabase());
        _logger = logger ?? NullLogger.Instance;
        _databaseWait = databaseWait ?? DefaultDatabaseWait;
    }

    /// <param name="runExclusively">
    /// runs what it is given while no other instance does the same on this database, which exists by then.
    /// </param>
    public SagaDbCreationOutcome EnsureCreated(Action<Action> runExclusively, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runExclusively);

        EnsureDatabaseExists(cancellationToken);

        // the same two checks EnsureCreated makes: a schema that is there costs nothing more than it did
        if (_creator.HasTables())
            return SagaDbCreationOutcome.AlreadyThere;

        // instances that wait for each other here run the same version: whichever creates the tables, they are current
        var outcome = SagaDbCreationOutcome.CreatedByAnotherInstance;
        runExclusively(() =>
        {
            if (_creator.HasTables())
                return;

            _creator.CreateTables();
            outcome = SagaDbCreationOutcome.Created;
        });

        return outcome;
    }

    // a lock inside a database that does not exist is not there to take: instances wait for each other where
    // the server has another way to, and otherwise whoever loses the race is told so, each server in its own way,
    // and goes on once the database can be used. Other failures are reported at once
    private void EnsureDatabaseExists(CancellationToken cancellationToken)
    {
        if (Exists())
            return;

        _runDatabaseCreationExclusively(() =>
        {
            // the instance this one waited for has created it
            _forgetFailedConnections();
            if (Exists())
                return;

            CreateDatabase(cancellationToken);
        });
    }

    private void CreateDatabase(CancellationToken cancellationToken)
    {
        try
        {
            _creator.Create();
        }
        catch (Exception e) when (_isBeingCreatedByAnotherInstance(e))
        {
            _logger.LogInformation(
                "The saga database is being created by another instance: waiting up to {Wait} for it. {Error}",
                _databaseWait,
                e.Message);

            if (!WaitUntilDatabaseExists(cancellationToken))
                throw;
        }
    }

    private bool WaitUntilDatabaseExists(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _databaseWait;

        while (true)
        {
            _forgetFailedConnections();

            if (Exists())
                return true;

            if (DateTimeOffset.UtcNow >= deadline)
                return false;

            if (cancellationToken.WaitHandle.WaitOne(PollingInterval))
                cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // a database that is being created can refuse connections for a moment
    private bool Exists()
    {
        try
        {
            return _creator.Exists();
        }
        catch
        {
            return false;
        }
    }
}
