using Microsoft.EntityFrameworkCore.Infrastructure;

namespace OpenSleigh.Persistence.SQL;

/// <summary>
/// creates the saga database and its schema, when missing, the first time a context asks for it,
/// and upgrades the schema of a database created by an earlier version of the library.
/// Registered as a singleton, it remembers the outcome for the lifetime of its service provider:
/// contexts are created several times per message, and only the first one pays for the check.
/// </summary>
public sealed class SagaDbSchemaInitializer
{
    private readonly object _gate = new();
    private readonly SagaDbSchemaUpgrade? _schemaUpgrade;
    private volatile bool _created;

    /// <param name="schemaUpgrade">
    /// what a database that already exists is brought up to date with. Without one, it is left as it is.
    /// </param>
    public SagaDbSchemaInitializer(SagaDbSchemaUpgrade? schemaUpgrade = null)
    {
        _schemaUpgrade = schemaUpgrade;
    }

    public void EnsureCreated(DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (_created)
            return;

        // contexts created concurrently at startup wait here instead of racing to create the same schema
        lock (_gate)
        {
            if (_created)
                return;

            // a database that was just created has the current schema
            var alreadyExisted = !database.EnsureCreated();
            if (alreadyExisted)
                _schemaUpgrade?.Apply(database);

            _created = true;
        }
    }
}
