using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.Mongo.Messaging;
using OpenSleigh.Persistence.Mongo.Utils;

namespace OpenSleigh.Persistence.Mongo
{
    public record MongoConfiguration(string ConnectionString,
                                     string DbName,
                                     MongoSagaStateRepositoryOptions RepositoryOptions,
                                     MongoOutboxProcessorOptions OutboxOptions,
                                     MongoOutboxCleanerOptions CleanerOptions);

    public static class MongoSagaConfiguratorExtensions
    {
        public static ISagaConfigurator<TS, TD> UseMongoPersistence<TS, TD>(
            this ISagaConfigurator<TS, TD> sagaConfigurator, MongoConfiguration config)
            where TS : Saga<TD>
            where TD : SagaState
        {
            sagaConfigurator.Services
                .AddSingleton<IMongoClient>(ctx => new MongoClient(connectionString: config.ConnectionString))
                .AddSingleton(ctx =>
                {
                    var client = ctx.GetRequiredService<IMongoClient>();
                    var database = client.GetDatabase(config.DbName);
                    return database;
                })
                .AddSingleton<ISerializer, JsonSerializer>()
                .AddSingleton<IDbContext, DbContext>()
                .AddSingleton<IUnitOfWork, MongoUnitOfWork>()
                .AddSingleton(config.RepositoryOptions)
                .AddSingleton<ISagaStateRepository, MongoSagaStateRepository>()
                .AddSingleton<IOutboxRepository, OutboxRepository>()
                .AddSingleton<IOutboxProcessor>(ctx =>
                {
                    var repo = ctx.GetRequiredService<IOutboxRepository>();
                    var publisher = ctx.GetRequiredService<IPublisher>();
                    var logger = ctx.GetRequiredService<ILogger<MongoOutboxProcessor>>();
                    return new MongoOutboxProcessor(repo, publisher, config.OutboxOptions, logger);
                })
                .AddSingleton<IOutboxCleaner>(ctx =>
                {
                    var repo = ctx.GetRequiredService<IOutboxRepository>();
                    return new MongoOutboxCleaner(repo, config.CleanerOptions);
                });
            return sagaConfigurator;
        }
    }
}