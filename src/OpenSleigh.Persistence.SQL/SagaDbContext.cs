using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQL
{
    public interface ISagaDbContext
    {
        DbSet<SagaState> SagaStates { get; set; }
    }

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
        }

        public DbSet<SagaState> SagaStates { get; set; }
    }
}