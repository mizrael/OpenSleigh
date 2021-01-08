using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.BackgroundServices;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOpenSleigh(this IServiceCollection services, Action<IBusConfigurator> configure = null)
        {
            var stateTypeResolver = new SagaTypeResolver();

            services.AddSingleton<ISagaTypeResolver>(stateTypeResolver)
                .AddSingleton<ISagasRunner, SagasRunner>()
                .AddSingleton<ITypesCache, TypesCache>()
                .AddSingleton<ITypeResolver>(ctx =>
                {
                    var resolver = new TypeResolver();

                    var sagaTypeResolver = ctx.GetRequiredService<ISagaTypeResolver>();
                    var sagaTypes = sagaTypeResolver.GetSagaTypes();
                    foreach (var t in sagaTypes)
                        resolver.Register(t);

                    return resolver;
                })
                .AddSingleton<IMessageContextFactory, DefaultMessageContextFactory>()
                .AddScoped<IMessageBus, DefaultMessageBus>()
                .AddSingleton<IMessageProcessor, MessageProcessor>()
                .AddSingleton<IOutboxProcessor>(ctx =>
                {
                    var repo = ctx.GetRequiredService<IOutboxRepository>();
                    var publisher = ctx.GetRequiredService<IPublisher>();
                    var logger = ctx.GetRequiredService<ILogger<OutboxProcessor>>();
                    return new OutboxProcessor(repo, publisher, OutboxProcessorOptions.Default, logger);
                })
                .AddSingleton<IOutboxCleaner>(ctx =>
                {
                    var repo = ctx.GetRequiredService<IOutboxRepository>();
                    return new OutboxCleaner(repo, OutboxCleanerOptions.Default);
                });

            var builder = new BusConfigurator(services, stateTypeResolver);
            configure?.Invoke(builder);

            services.AddHostedService<SubscribersBackgroundService>()
                   .AddHostedService<OutboxBackgroundService>()
                   .AddHostedService<OutboxCleanerBackgroundService>();

            return services;
        }
    }

}