using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Queries;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.PostgreSQL;

[ExcludeFromCodeCoverage]
public static class SqlBusConfiguratorExtensions
{
    public static IBusConfigurator UsePostgreSqlPersistence(
        this IBusConfigurator busConfigurator, SqlConfiguration config)
    {
        busConfigurator.Services
            .AddSingleton(config.SagaRepositoryOptions)
            .AddSingleton(config.OutboxRepositoryOptions)
            .AddSingleton<SagaDbSchemaInitializer>()
            .AddPooledSagaDbContext(builder => builder.UseNpgsql(config.ConnectionString))
            // the relay reads and deletes outbox rows on the context its transaction runs on,
            // so that the rows it reads stay locked, and are skipped by other relays, until it is done with them
            .AddScoped<SqlTransactionManager>()
            .AddScoped<ITransactionManager>(ctx => ctx.GetRequiredService<SqlTransactionManager>())
            .AddSingleton<DuplicateKeyDetector>(IsDuplicateKeyException)
            .AddTransient<IOutboxRepository>(ctx => new PostgreSQLOutboxRepository(
                ctx.GetRequiredService<SqlOutboxRepositoryOptions>(),
                ctx.GetRequiredService<SagaDbContext>(),
                ctx.GetRequiredService<OpenSleigh.Utils.ITypeResolver>(),
                ctx.GetRequiredService<OpenSleigh.Utils.ISerializer>(),
                ctx.GetRequiredService<DuplicateKeyDetector>(),
                () => ctx.GetRequiredService<SqlTransactionManager>().DbContext))
            .AddTransient<ISagaStateRepository, SqlSagaStateRepository>()
            .AddTransient<ISagaStateQuery, SqlSagaStateQuery>();
        
        return busConfigurator;
    }

    internal static bool IsDuplicateKeyException(Exception ex)
    => ex switch
    {
        DbUpdateException dbEx => IsDuplicateKeyException(dbEx),
        InvalidOperationException opEx => IsDuplicateKeyException(opEx),
        _ => false
    };

    // TODO: this sucks hard
    private static bool IsDuplicateKeyException(InvalidOperationException ex)
    => ex.Source == "Microsoft.EntityFrameworkCore" &&
            ex.Message.Contains("cannot be tracked because another instance with the");
    
    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pgEx)
        {
            // Postgres error code for unique violation
            return pgEx.SqlState == "23505";
        }

        return false;
    }
}
