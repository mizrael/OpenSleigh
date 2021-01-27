using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace OpenSleigh.Persistence.SQL.Tests.Fixtures
{
    public class DbFixture : IDisposable
    {
        public DbFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var connStr = configuration.GetConnectionString("sql");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new ArgumentException("invalid connection string");

            this.ConnectionString = string.Format(connStr, Guid.NewGuid());
            
            var options = new DbContextOptionsBuilder<SagaDbContext>()
                .UseSqlServer(this.ConnectionString)
                .EnableSensitiveDataLogging()
                .Options;
            _dbContext = new SagaDbContext(options);
        }
        
        public string ConnectionString { get; }

        private readonly SagaDbContext _dbContext;
        public ISagaDbContext DbContext => _dbContext;

        public void Dispose()
        {
            _dbContext?.Database.EnsureDeleted();
        }
    }
}
