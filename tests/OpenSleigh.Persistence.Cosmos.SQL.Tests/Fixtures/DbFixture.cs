using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures
{
    public class DbFixture : IAsyncLifetime
    {
        private readonly List<SagaDbContext> _dbContexts = new();
        
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
        }
        
        public string ConnectionString { get; }

        public (ISagaDbContext dbContext, string dbName) CreateDbContext()
        {
            var dbName = $"tests_{Guid.NewGuid()}";
            var dbContextOptions = new DbContextOptionsBuilder<SagaDbContext>()
                .UseCosmos(this.ConnectionString, dbName)
                .EnableSensitiveDataLogging()
                .Options;
            var ctx = new SagaDbContext(dbContextOptions);
            _dbContexts.Add(ctx);
            return (ctx, dbName);
        }

        public void Dispose()
        {
            foreach(var ctx in _dbContexts)
                ctx.Database.EnsureDeleted();
        }

        public Task InitializeAsync()
            => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            foreach (var ctx in _dbContexts)                
                await ctx.Database.EnsureDeletedAsync();

            //uncomment this if too may dbs remain hanging.
            //using var client = new Microsoft.Azure.Cosmos.CosmosClient (ConnectionString);
            //var iterator = client.GetDatabaseQueryIterator<Microsoft.Azure.Cosmos.DatabaseProperties>();
            //do
            //{
            //    foreach (var db in await iterator.ReadNextAsync())
            //    {
            //        var dbContextOptions = new DbContextOptionsBuilder<SagaDbContext>()
            //            .UseCosmos(this.ConnectionString, db.Id)              
            //            .Options;
            //        var ctx = new SagaDbContext(dbContextOptions);
            //        ctx.Database.EnsureDeleted();
            //    }
            //} while (iterator.HasMoreResults);
        }
    }
}
