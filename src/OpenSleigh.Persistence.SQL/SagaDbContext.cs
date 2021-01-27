using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQL
{
    public interface ISagaDbContext
    {
        DbSet<SagaState> SagaStates { get; set; }
        DbSet<OutboxMessage> OutboxMessages { get; set; }

        Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default);
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
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

        public async Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await this.Database.BeginTransactionAsync(cancellationToken);
            return new SqlTransaction(transaction);
        }

        public DbSet<SagaState> SagaStates { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
    }
}