using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    [ExcludeFromCodeCoverage]
    public record SqlConfiguration(string ConnectionString);
    
    [ExcludeFromCodeCoverage]
    public static class SqlBusConfiguratorExtensions
    {
        public static IBusConfigurator UseSqlPersistence(
            this IBusConfigurator busConfigurator, SqlConfiguration config)
        {
            busConfigurator.Services.AddDbContextPool<SagaDbContext>(builder =>
            {
                builder.UseSqlServer(config.ConnectionString);
            }).AddScoped<ISagaDbContext>(ctx => ctx.GetRequiredService<SagaDbContext>())
            .AddSingleton<ITransactionManager, SqlTransactionManager>()
            .AddSingleton<IOutboxRepository, SqlOutboxRepository>()
            .AddSingleton<ISagaStateRepository, SqlSagaStateRepository>();
            
            return busConfigurator;
        }
    }
}