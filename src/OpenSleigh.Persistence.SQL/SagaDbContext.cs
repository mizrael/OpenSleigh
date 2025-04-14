using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL.Entities;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public class SagaDbContext : DbContext
{
    public SagaDbContext(DbContextOptions<SagaDbContext> options)
        : base(options)
    {
        Database.EnsureCreated(); 
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SagaStateEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageStateEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new SagaProcessedMessageTypeConfiguration());
    }

    public DbSet<SagaState> SagaStates { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
}