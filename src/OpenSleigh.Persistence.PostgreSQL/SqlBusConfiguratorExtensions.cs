using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;
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
            .AddDbContext<SagaDbContext>(builder =>
            {
                builder.UseNpgsql(config.ConnectionString);
            }, contextLifetime: ServiceLifetime.Transient)
            .AddScoped<ITransactionManager, SqlTransactionManager>()
            .AddSingleton<DuplicateKeyDetector>(IsDuplicateKeyException)
            .AddTransient<IOutboxRepository, PostgreSQLOutboxRepository>()
            .AddTransient<ISagaStateRepository, SqlSagaStateRepository>();
        
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
