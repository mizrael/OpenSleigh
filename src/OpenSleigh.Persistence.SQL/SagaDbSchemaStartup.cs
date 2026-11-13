using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace OpenSleigh.Persistence.SQL;

/// <summary>
/// creates or upgrades the saga database when the host starts, before any hosted service does:
/// a database that cannot be brought up to date stops the application from starting,
/// instead of failing the first message that needs it.
/// </summary>
internal sealed class SagaDbSchemaStartup : IHostedLifecycleService
{
    private readonly IDbContextFactory<SagaDbContext> _dbContextFactory;
    private readonly SagaDbSchemaInitializer _schemaInitializer;

    public SagaDbSchemaStartup(IDbContextFactory<SagaDbContext> dbContextFactory, SagaDbSchemaInitializer schemaInitializer)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _schemaInitializer = schemaInitializer ?? throw new ArgumentNullException(nameof(schemaInitializer));
    }

    // not on the thread that starts the host: the initializer waits for the database
    public Task StartingAsync(CancellationToken cancellationToken)
        => Task.Run(EnsureSchema, cancellationToken);

    private void EnsureSchema()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        _schemaInitializer.EnsureCreated(dbContext.Database);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
