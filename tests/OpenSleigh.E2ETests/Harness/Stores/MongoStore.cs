using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Persistence.Mongo.Entities;

namespace OpenSleigh.E2ETests.Harness.Stores;

/// <summary>
/// a database per run, on a server shared by the suite.
/// </summary>
internal sealed class MongoStore : IStore
{
    private const string ConnectionStringName = "mongo";
    private const string DatabaseNamePrefix = "OpenSleighTests_";

    private readonly string _connectionString;
    private readonly string _databaseName;

    public MongoStore(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _connectionString = context.GetRequiredConnectionString(ConnectionStringName);
        _databaseName = DatabaseNamePrefix + context.RunId;
    }

    public void Configure(IBusConfigurator bus)
    {
        bus.UseMongoPersistence(new MongoConfiguration(_connectionString, _databaseName));

        // KNOWN DEFECT WORKAROUND (KnownDefects.MongoRegistersNoTransactionManager): remove with the defect.
        // It adds nothing once UseMongoPersistence registers a transaction manager of its own
        bus.Services.TryAddSingleton<ITransactionManager, NoTransactionManager>();
    }

    public Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long> CountOutboxMessagesAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
        => hostServices.GetRequiredService<IDbContext>()
            .OutboxMessages
            .CountDocumentsAsync(FilterDefinition<OutboxMessage>.Empty, cancellationToken: cancellationToken);

    public async ValueTask DisposeAsync()
    {
        using var client = new MongoClient(_connectionString);
        await client.DropDatabaseAsync(_databaseName);
    }

    private sealed class NoTransactionManager : ITransactionManager
    {
        public ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ITransaction>(new NoTransaction());
    }

    private sealed class NoTransaction : ITransaction
    {
        public ValueTask CommitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
