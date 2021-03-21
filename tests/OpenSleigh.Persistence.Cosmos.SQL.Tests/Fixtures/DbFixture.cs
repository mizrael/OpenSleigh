using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures
{
    public class DbFixture : IDisposable
    {
        public DbFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)                
                .AddEnvironmentVariables()
                .AddUserSecrets<DbFixture>()
                .Build();

            this.ConnectionString = configuration.GetConnectionString("cosmosSQL");
            if (string.IsNullOrWhiteSpace(this.ConnectionString))
                throw new ArgumentException("invalid connection string");

            this.DbName = $"tests_{Guid.NewGuid()}";
            
            var options = new DbContextOptionsBuilder<SagaDbContext>()
                .UseCosmos(this.ConnectionString, this.DbName)
                .EnableSensitiveDataLogging()
                .Options;
            _dbContext = new SagaDbContext(options);
        }
        
        public string ConnectionString { get; }
        public string DbName{ get; }

        private readonly SagaDbContext _dbContext;
        public ISagaDbContext DbContext => _dbContext;

        public void Dispose()
        {
            _dbContext?.Database.EnsureDeleted();
        }
    }
}
