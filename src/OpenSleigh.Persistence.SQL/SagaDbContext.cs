using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Persistence.SQL.Entities;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public class SagaDbContext : DbContext
{
    /// <summary>
    /// creates a context and makes sure its database and schema exist.
    /// When the context comes from a service provider that has a <see cref="SagaDbSchemaInitializer"/>
    /// (the <c>Use...Persistence</c> methods register one), the check runs once for that provider.
    /// Otherwise it runs for every context.
    /// </summary>
    /// <remarks>
    /// There is one public constructor, taking the options only, on purpose: that is what
    /// AddDbContextPool, AddDbContextFactory and AddPooledDbContextFactory require.
    /// </remarks>
    public SagaDbContext(DbContextOptions<SagaDbContext> options)
        : base(options)
    {
        var schemaInitializer = options.FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider?
            .GetService<SagaDbSchemaInitializer>();

        if (schemaInitializer is null)
            Database.EnsureCreated();
        else
            schemaInitializer.EnsureCreated(Database);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SagaStateEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageStateEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new SagaProcessedMessageTypeConfiguration());
        modelBuilder.ApplyConfiguration(new SagaFailureRecordTypeConfiguration());
    }

    public async ValueTask<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await this.Database.BeginTransactionAsync(cancellationToken);
        return new SqlTransaction(transaction);
    }

    public DbSet<SagaState> SagaStates { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<SagaFailureRecord> SagaMessageFailures { get; set; }
}
