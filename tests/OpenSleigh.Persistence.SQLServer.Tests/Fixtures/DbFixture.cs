using Microsoft.Extensions.Configuration;
using OpenSleigh.Persistence.SQL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.SQLServer.Tests.Fixtures
{
    public class DbFixture : IAsyncLifetime
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

        public Task InitializeAsync() => Task.CompletedTask;
        
        public async Task DisposeAsync()
        {
            var queue = new Queue<SagaDbContext>(_dbContexts);
            while (queue.Any())
            {
                var ctx = queue.Dequeue();
                try
                {
                    await ctx.Database.EnsureDeletedAsync();
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
