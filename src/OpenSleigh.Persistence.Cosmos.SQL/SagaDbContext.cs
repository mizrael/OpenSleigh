﻿using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Persistence.Cosmos.SQL.Entities;

namespace OpenSleigh.Persistence.Cosmos.SQL
{
    public interface ISagaDbContext
    {
        DbSet<SagaState> SagaStates { get; set; }
        DbSet<OutboxMessage> OutboxMessages { get; set; }

        Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default);
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }

    [ExcludeFromCodeCoverage]
    public class SagaDbContext : DbContext, ISagaDbContext
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
        }

        public Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new NullTransaction() as ITransaction); // https://github.com/dotnet/efcore/issues/16836

        public DbSet<SagaState> SagaStates { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
    }
}