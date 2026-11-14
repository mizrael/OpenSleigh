using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL;

namespace OpenSleigh.E2ETests.Harness.Stores;

/// <summary>
/// a database per run, on a server shared by the suite.
/// </summary>
internal abstract class SqlStore : IStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaCreated;

    /// <param name="connectionStringName">a connection string with a placeholder for the name of the database.</param>
    protected SqlStore(RunContext context, string connectionStringName)
    {
        ArgumentNullException.ThrowIfNull(context);
        _connectionString = string.Format(context.GetRequiredConnectionString(connectionStringName), context.RunId);
    }

    public void Configure(IBusConfigurator bus) => UsePersistence(bus, new SqlConfiguration(_connectionString));

    protected abstract void UsePersistence(IBusConfigurator bus, SqlConfiguration configuration);

    protected abstract void UseProvider(DbContextOptionsBuilder options, string connectionString);

    // the first host creates the database and its schema, before any host starts.
    // The others find them there: their own check, at their first context, changes nothing
    public async Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
    {
        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_schemaCreated)
                return;

            // a context makes sure its database and schema exist when it is created
            await using var scope = hostServices.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<SagaDbContext>();
            _schemaCreated = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    public async Task<long> CountOutboxMessagesAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
    {
        await using var scope = hostServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        return await dbContext.OutboxMessages.LongCountAsync(cancellationToken);
    }

    // through a context with no model: one of the library would create the database it is about to drop
    public async ValueTask DisposeAsync()
    {
        var options = new DbContextOptionsBuilder();
        UseProvider(options, _connectionString);

        await using var database = new DbContext(options.Options);
        await database.Database.EnsureDeletedAsync();
    }
}
