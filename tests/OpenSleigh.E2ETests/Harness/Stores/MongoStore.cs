using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using OpenSleigh.DependencyInjection;
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
    private readonly SemaphoreSlim _indexesGate = new(1, 1);
    private bool _indexesCreated;

    public MongoStore(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _connectionString = context.GetRequiredConnectionString(ConnectionStringName);
        _databaseName = DatabaseNamePrefix + context.RunId;
    }

    public void Configure(IBusConfigurator bus)
        => UseMongoPersistence(bus, _connectionString, _databaseName);

    internal static void UseMongoPersistence(IBusConfigurator bus, string connectionString, string databaseName)
        => bus.UseMongoPersistence(new MongoConfiguration(connectionString, databaseName));

    // the first host creates the collections and their indexes, before any host starts: a context does that when it is created
    public async Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
    {
        await _indexesGate.WaitAsync(cancellationToken);
        try
        {
            if (_indexesCreated)
                return;

            hostServices.GetRequiredService<IDbContext>();
            _indexesCreated = true;
        }
        finally
        {
            _indexesGate.Release();
        }
    }

    public Task<long> CountOutboxMessagesAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
        => hostServices.GetRequiredService<IDbContext>()
            .OutboxMessages
            .CountDocumentsAsync(FilterDefinition<OutboxMessage>.Empty, cancellationToken: cancellationToken);

    public async ValueTask DisposeAsync()
    {
        using var client = new MongoClient(_connectionString);
        await client.DropDatabaseAsync(_databaseName);
    }
}
