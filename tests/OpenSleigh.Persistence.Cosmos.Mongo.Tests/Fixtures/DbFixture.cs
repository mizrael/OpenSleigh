using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.Mongo.Tests.Fixtures
{
    public class DbFixture : IAsyncLifetime
    {
        private readonly MongoClient _client;
        private readonly List<string> _dbNames = new();
        
        public DbFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .AddUserSecrets<DbFixture>()
                .Build();

            this.ConnectionString = configuration.GetConnectionString("cosmosMongo");
            if (string.IsNullOrWhiteSpace(this.ConnectionString))
                throw new ArgumentException("invalid connection string");

            var settings = MongoClientSettings.FromUrl(new MongoUrl(this.ConnectionString));
            settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };

            _client = new MongoClient(settings);
        }

        public (IDbContext dbContext, string dbName) CreateDbContext()
        {
            var dbName = $"openSleigh_{Guid.NewGuid()}";
            var db = _client.GetDatabase(dbName);

            var dbContext = new DbContext(db);
            
            _dbNames.Add(dbName);
            
            return (dbContext, dbName);
        }
        
        public string ConnectionString { get; }

        public Task InitializeAsync()
            => Task.CompletedTask;
      
        public async Task DisposeAsync()
        {
            if (_client is null)
                return;

            var queue = new Queue<string>(_dbNames);
            while (queue.Any())
            {
                var dbName = queue.Dequeue();
                try
                {
                    await _client.DropDatabaseAsync(dbName);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    queue.Enqueue(dbName);
                }
            }

            //uncomment this if too may dbs remain hanging.
            //var dbs = await _client.ListDatabaseNamesAsync();
            //foreach (var dbName in dbs.ToList())
            //    await _client.DropDatabaseAsync(dbName);
        }
    }
}
