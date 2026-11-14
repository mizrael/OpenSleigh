using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Queries;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQLServer;

[ExcludeFromCodeCoverage]
public static class SqlBusConfiguratorExtensions
{
    public static IBusConfigurator UseSqlServerPersistence(
        this IBusConfigurator busConfigurator, SqlConfiguration config)
    {
        busConfigurator.Services
            .AddSingleton(config.SagaRepositoryOptions)
            .AddSingleton(config.OutboxRepositoryOptions)
            .AddSagaDbSchemaInitialization<SqlServerSchemaUpgrade>()
            .AddPooledSagaDbContext(builder => builder.UseSqlServer(config.ConnectionString))
            // the relay reads and deletes outbox rows on the context its transaction runs on,
            // so that the rows it reads stay locked, and are skipped by other relays, until it is done with them
            .AddScoped<SqlTransactionManager>()
            .AddScoped<ITransactionManager>(ctx => ctx.GetRequiredService<SqlTransactionManager>())
            .AddSingleton<DuplicateKeyDetector>(IsDuplicateKeyException)
            .AddTransient<IOutboxRepository>(ctx => new MSSqlOutboxRepository(
                ctx.GetRequiredService<SagaDbContext>(),
                ctx.GetRequiredService<OpenSleigh.Utils.ITypeResolver>(),
                ctx.GetRequiredService<SqlOutboxRepositoryOptions>(),
                ctx.GetRequiredService<OpenSleigh.Utils.ISerializer>(),
                ctx.GetRequiredService<DuplicateKeyDetector>(),
                () => ctx.GetRequiredService<SqlTransactionManager>().DbContext))
            .AddTransient<ISagaStateRepository, SqlSagaStateRepository>()
            .AddTransient<ISagaStateQuery, SqlSagaStateQuery>()
            .AddTransient<ISagaFailureQuery, SqlSagaFailureQuery>()
            .AddTransient<ISagaFailurePurger, SqlSagaFailureQuery>();
        
        return busConfigurator;
    }

    internal static bool IsDuplicateKeyException(Exception ex)
    => ex switch
    {
        DbUpdateException dbEx => IsDuplicateKeyException(dbEx),
        InvalidOperationException opEx => IsDuplicateKeyException(opEx),
        _ => false
    };

    private static bool IsDuplicateKeyException(InvalidOperationException opEx)
    => opEx.Source == "Microsoft.EntityFrameworkCore" &&
        opEx.Message.Contains("cannot be tracked because another instance with the");

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx)
        {
            // SQL Server error codes for duplicate key violations
            return sqlEx.Number == 2627 || sqlEx.Number == 2601;
        }

        return false;
    }
}
