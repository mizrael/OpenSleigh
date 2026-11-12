using Microsoft.EntityFrameworkCore.Infrastructure;

namespace OpenSleigh.Persistence.SQL;

/// <summary>
/// creates the saga database and its schema, when missing, the first time a context asks for it.
/// Registered as a singleton, it remembers the outcome for the lifetime of its service provider:
/// contexts are created several times per message, and only the first one pays for the check.
/// </summary>
public sealed class SagaDbSchemaInitializer
{
    private readonly object _gate = new();
    private volatile bool _created;

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

            database.EnsureCreated();
            _created = true;
        }
    }
}
