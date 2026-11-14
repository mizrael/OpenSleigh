using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Queries;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.Mongo;

[ExcludeFromCodeCoverage]
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
            .AddSingleton(config.SagaRepositoryOptions)
            .AddSingleton(config.OutboxRepositoryOptions)

            // one context per service provider: it only holds the collections, which are safe to share,
            // and creating it asks the server to create the indexes
            .AddSingleton<IDbContext, DbContext>()
            .AddTransient<ISagaStateRepository, MongoSagaStateRepository>()
            .AddTransient<IOutboxRepository, MongoOutboxRepository>()
            .AddTransient<ISagaStateQuery, MongoSagaStateQuery>();

        // the relay asks for a transaction around each pass. On MongoDB there is none to give: a relay claims
        // the messages it reads instead, and whatever it does not delete is read again when the claim expires.
        // Not replaced when the application, or a transport, has registered its own
        busConfigurator.Services.TryAddSingleton<ITransactionManager, NoOpTransactionManager>();

        return busConfigurator;
    }
}