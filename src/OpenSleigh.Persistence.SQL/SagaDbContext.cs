using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL.Entities;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public class SagaDbContext : DbContext
{
    /// <summary>
    /// creates a context and makes sure its database and schema exist, checking on every call.
    /// </summary>
    public SagaDbContext(DbContextOptions<SagaDbContext> options)
        : base(options)
    {
        Database.EnsureCreated();
    }

    /// <summary>
    /// creates a context whose database and schema are checked once per <paramref name="schemaInitializer"/>.
    /// Dependency injection picks this constructor when the initializer is registered.
    /// </summary>
    public SagaDbContext(DbContextOptions<SagaDbContext> options, SagaDbSchemaInitializer schemaInitializer)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(schemaInitializer);
        schemaInitializer.EnsureCreated(Database);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SagaStateEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageStateEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new SagaProcessedMessageTypeConfiguration());
    }

    public async ValueTask<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await this.Database.BeginTransactionAsync(cancellationToken);
        return new SqlTransaction(transaction);
    }

    public DbSet<SagaState> SagaStates { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
}
