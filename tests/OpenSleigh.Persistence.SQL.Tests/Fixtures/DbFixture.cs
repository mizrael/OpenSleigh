using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Fixtures
{
    public abstract class DbFixture : IAsyncLifetime
    {
        private readonly string _connStrTemplate;
        private readonly List<SagaDbContext> _dbContexts = new();
        
        protected DbFixture(string connectionStringSectionName)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            _connStrTemplate = configuration.GetConnectionString(connectionStringSectionName);
            if (string.IsNullOrWhiteSpace(_connStrTemplate))
                throw new ArgumentException("invalid connection string");
        }

        protected abstract DbContextOptionsBuilder<SagaDbContext> CreateOptionsBuilder(string connectionString);

        public (ISagaDbContext db, string connStr) CreateDbContext()
        {
            var connectionString = string.Format(_connStrTemplate, Guid.NewGuid());

            var optionsBuilder = CreateOptionsBuilder(connectionString);
            var options = optionsBuilder.Options;

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
