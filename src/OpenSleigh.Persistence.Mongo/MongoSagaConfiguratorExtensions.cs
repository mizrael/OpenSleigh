using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using OpenSleigh.Core.BackgroundServices;

namespace OpenSleigh.Persistence.Mongo
{
    public record MongoConfiguration(string ConnectionString,
                                     string DbName,
                                     MongoSagaStateRepositoryOptions RepositoryOptions);

    public static class MongoSagaConfiguratorExtensions
    {
        public static ISagaConfigurator<TS, TD> UseMongoPersistence<TS, TD>(
            this ISagaConfigurator<TS, TD> sagaConfigurator, MongoConfiguration config)
            where TS : Saga<TD>
            where TD : SagaState
        {
            sagaConfigurator.Services.AddSingleton(ctx => new MongoClient(connectionString: config.ConnectionString))
                .AddSingleton(ctx =>
                {
                    var client = ctx.GetRequiredService<MongoClient>();
                    var database = client.GetDatabase(config.DbName);
                    return database;
                })
                .AddSingleton<ISerializer, JsonSerializer>()
                .AddSingleton<IDbContext, DbContext>()
                .AddSingleton<IUnitOfWork, MongoUnitOfWork>()
                .AddSingleton(config.RepositoryOptions)
                .AddSingleton<ISagaStateRepository, MongoSagaStateRepository>()
                .AddSingleton<IOutboxRepository, OutboxRepository>()
                .AddSingleton<IMessagePublisherService, MongoMessagePublisherService>();
            return sagaConfigurator;
        }
    }
}