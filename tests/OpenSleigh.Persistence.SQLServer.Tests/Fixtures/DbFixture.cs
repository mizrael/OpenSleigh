using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenSleigh.Persistence.SQL;

namespace OpenSleigh.Persistence.SQLServer.Tests.Fixtures
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
            var queue = new Queue<SagaDbContext>(_dbContexts);
            while (queue.Any())
            {
                var ctx = queue.Dequeue();
                try
                {
                    ctx.Database.EnsureDeleted();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    queue.Enqueue(ctx);
                }
            }
        }
    }
}
