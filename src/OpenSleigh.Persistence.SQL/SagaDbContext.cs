using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQL
{
    internal class SagaDbContext : DbContext
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