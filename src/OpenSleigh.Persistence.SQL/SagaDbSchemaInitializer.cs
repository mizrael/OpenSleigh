using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Transactions;

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

    public void EnsureCreated(DatabaseFacade database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (_created)
            return;

        // contexts created concurrently at startup wait here instead of racing to create the same schema
        lock (_gate)
        {
            if (_created)
                return;

            // creating and upgrading the schema open connections and transactions of their own:
            // a transaction of whoever asked for the first context is not theirs to take part in
            using (new TransactionScope(TransactionScopeOption.Suppress))
            {
                // a database that was just created has the current schema
                var alreadyExisted = !EnsureDatabaseCreated(database, cancellationToken);
                if (alreadyExisted)
                    _schemaUpgrade?.Apply(database);
            }

            _created = true;
        }
    }

    // instances that start together on a database that is missing, or empty, wait for each other
    // through the schema upgrade, which knows how to on its database
    private bool EnsureDatabaseCreated(DatabaseFacade database, CancellationToken cancellationToken)
    {
        if (_schemaUpgrade is null || database.GetService<IDatabaseCreator>() is not IRelationalDatabaseCreator creator)
            return database.EnsureCreated();

        var outcome = _schemaUpgrade.EnsureCreated(database, creator, cancellationToken);
        Seed(database, created: outcome == SagaDbCreationOutcome.Created);

        return outcome != SagaDbCreationOutcome.AlreadyThere;
    }

    // what EnsureCreated does once the database is there, for a context that was configured with UseSeeding
    private static void Seed(DatabaseFacade database, bool created)
    {
        var seeder = database.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Seeder;
        seeder?.Invoke(database.GetService<ICurrentDbContext>().Context, created);
    }
}
