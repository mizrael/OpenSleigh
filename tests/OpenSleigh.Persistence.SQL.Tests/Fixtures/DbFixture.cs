using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace OpenSleigh.Persistence.SQL.Tests.Fixtures
{
    public class DbFixture : IDisposable
    {
        private readonly string _connStrTemplate;
        private readonly List<SagaDbContext> _dbContexts = new();
        
        public DbFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            _connStrTemplate = configuration.GetConnectionString("sql");
            if (string.IsNullOrWhiteSpace(_connStrTemplate))
                throw new ArgumentException("invalid connection string");
        }
        
        public (ISagaDbContext db, string connStr) CreateDbContext()
        {
            var connectionString = string.Format(_connStrTemplate, Guid.NewGuid());
            
            var options = new DbContextOptionsBuilder<SagaDbContext>()
                .UseSqlServer(connectionString)
                .EnableSensitiveDataLogging()
                .Options;
            var dbContext = new SagaDbContext(options);
            _dbContexts.Add(dbContext);
            return (dbContext, connectionString);
        }

        public void Dispose()
        {
            foreach (var ctx in _dbContexts)
            {
                try
                {
                    ctx.Database.EnsureDeleted();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
        }
    }
}
