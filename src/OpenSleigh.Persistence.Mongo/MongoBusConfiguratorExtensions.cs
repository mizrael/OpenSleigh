using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using OpenSleigh.Persistence.Mongo.Messaging;
using OpenSleigh.Persistence.Mongo.Utils;

namespace OpenSleigh.Persistence.Mongo
{
    public record MongoConfiguration(string ConnectionString,
                                     string DbName,
                                     MongoSagaStateRepositoryOptions SagaRepositoryOptions,
                                     MongoOutboxRepositoryOptions OutboxRepositoryOptions);

    public static class MongoBusConfiguratorExtensions
    {
        public static IBusConfigurator UseMongoPersistence(
            this IBusConfigurator busConfigurator, MongoConfiguration config)
        {
            busConfigurator.Services
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
                .AddSingleton(config.SagaRepositoryOptions)
                .AddSingleton(config.OutboxRepositoryOptions)
                .AddSingleton<ISagaStateRepository, MongoSagaStateRepository>()
                .AddSingleton<IOutboxRepository, MongoOutboxRepository>();
            return busConfigurator;
        }
    }
}