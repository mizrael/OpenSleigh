using Microsoft.EntityFrameworkCore.Storage;

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
    private readonly TimeSpan _databaseWait;

    /// <param name="databaseWait">
    /// how long to wait for a database that another instance is creating, once creating it here has failed.
    /// </param>
    public SagaDbCreation(IRelationalDatabaseCreator creator, TimeSpan? databaseWait = null)
    {
        _creator = creator ?? throw new ArgumentNullException(nameof(creator));
        _databaseWait = databaseWait ?? DefaultDatabaseWait;
    }

    /// <param name="runExclusively">
    /// runs what it is given while no other instance does the same on this database, which exists by then.
    /// </param>
    /// <returns>true when the tables were created by this call.</returns>
    public bool EnsureCreated(Action<Action> runExclusively)
    {
        ArgumentNullException.ThrowIfNull(runExclusively);

        EnsureDatabaseExists();

        // the same two checks EnsureCreated makes: a schema that is there costs nothing more than it did
        if (_creator.HasTables())
            return false;

        var created = false;
        runExclusively(() =>
        {
            // another instance may have created them while this one waited for its turn
            if (_creator.HasTables())
                return;

            _creator.CreateTables();
            created = true;
        });

        return created;
    }

    // nothing can make instances wait for each other before the database exists: whoever loses the race
    // is told that it already exists, in a way that depends on the server, and goes on once it does
    private void EnsureDatabaseExists()
    {
        if (_creator.Exists())
            return;

        try
        {
            _creator.Create();
        }
        catch (Exception) when (WaitUntilDatabaseExists())
        {
        }
    }

    private bool WaitUntilDatabaseExists()
    {
        var deadline = DateTimeOffset.UtcNow + _databaseWait;

        while (!Exists())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                return false;

            Thread.Sleep(PollingInterval);
        }

        return true;
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
