using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL;

namespace OpenSleigh.E2ETests.Harness.Stores;

/// <summary>
/// a database per run, on a server shared by the suite. The database and its schema are created
/// before the first host starts: several hosts creating them at the same time is a scenario of its own.
/// </summary>
internal abstract class SqlStore : IStore
{
    private const int DropAttempts = 3;
    private static readonly TimeSpan DropRetryDelay = TimeSpan.FromMilliseconds(500);

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

    public async ValueTask DisposeAsync()
    {
        if (!_schemaCreated)
            return;

        // the hosts are gone by now, with their services: the database is dropped through services of its own
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenSleigh(Configure);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();

        for (var attempt = 1; attempt <= DropAttempts; attempt++)
        {
            try
            {
                await dbContext.Database.EnsureDeletedAsync();
                return;
            }
            catch (Exception) when (attempt < DropAttempts)
            {
                // a connection of a host that has just stopped can still be closing
                await Task.Delay(DropRetryDelay);
            }
        }
    }
}
