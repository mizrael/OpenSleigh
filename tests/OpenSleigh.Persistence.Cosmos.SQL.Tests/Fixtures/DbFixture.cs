using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenSleigh.Transport.AzureServiceBus;

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

            var sbConnStr = configuration.GetConnectionString("AzureServiceBus");
            if (string.IsNullOrWhiteSpace(sbConnStr))
                throw new ArgumentException("missing Service Bus connection");
            
            this.AzureServiceBusConfiguration = new AzureServiceBusConfiguration(sbConnStr);

            this.DbName = $"tests_{Guid.NewGuid()}";
            
            _dbContextOptions = new DbContextOptionsBuilder<SagaDbContext>()
                .UseCosmos(this.ConnectionString, this.DbName)
                .EnableSensitiveDataLogging()
                .Options;
        }
        
        public string ConnectionString { get; }
        public string DbName{ get; }

        public AzureServiceBusConfiguration AzureServiceBusConfiguration { get; }

        private readonly DbContextOptions<SagaDbContext> _dbContextOptions;
        public ISagaDbContext CreateDbContext() => new SagaDbContext(_dbContextOptions);

        public void Dispose()
        {
            var dbContext = new SagaDbContext(_dbContextOptions);
            dbContext.Database.EnsureDeleted();
        }
    }
}
