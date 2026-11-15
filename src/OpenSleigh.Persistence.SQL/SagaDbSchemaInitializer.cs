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

            // creating and upgrading the schema open connections and transactions of their own:
            // a transaction of whoever asked for the first context is not theirs to take part in
            using (new TransactionScope(TransactionScopeOption.Suppress))
                EnsureCreatedAndCurrent(database);

            _created = true;
        }
    }

    // instances that start together on a database that is missing, or empty, wait for each other
    // through the schema upgrade, which knows how to on its database
    private void EnsureCreatedAndCurrent(DatabaseFacade database)
    {
        if (_schemaUpgrade is null || database.GetService<IDatabaseCreator>() is not IRelationalDatabaseCreator creator)
        {
            // a database that was just created has the current schema
            if (!database.EnsureCreated())
                _schemaUpgrade?.Apply(database);
            return;
        }

        _schemaUpgrade.EnsureCreatedAndCurrent(database, creator, FindSeeding(database));
    }

    // what EnsureCreated does once the database is there, for a context that was configured with UseSeeding
    private static Action<bool>? FindSeeding(DatabaseFacade database)
    {
        var options = database.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();

        if (options?.Seeder is { } seeder)
            return created =>
            {
                using var transaction = database.BeginTransaction();
                seeder(database.GetService<ICurrentDbContext>().Context, created);
                transaction.Commit();
            };

        if (options?.AsyncSeeder is not null)
            throw new InvalidOperationException(
                $"The saga database is seeded when the first context is created, which cannot wait for '{nameof(DbContextOptionsBuilder.UseAsyncSeeding)}': " +
                $"configure '{nameof(DbContextOptionsBuilder.UseSeeding)}' as well.");

        return null;
    }
}
