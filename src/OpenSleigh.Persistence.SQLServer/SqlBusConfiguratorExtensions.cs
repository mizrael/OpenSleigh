using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;
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
            .AddDbContext<SagaDbContext>(builder =>
            {
                builder.UseSqlServer(config.ConnectionString);
            }, contextLifetime: ServiceLifetime.Transient)
            .AddSingleton<DuplicateKeyDetector>(IsDuplicateKeyException)
            .AddTransient<IOutboxRepository, SqlOutboxRepository>()
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

    private static bool IsDuplicateKeyException(InvalidOperationException opEx)
    => (opEx.Source == "Microsoft.EntityFrameworkCore" &&
                 opEx.Message.Contains($"The instance of entity type '{nameof(OutboxMessage)}' cannot be tracked because another instance with the key value"));


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