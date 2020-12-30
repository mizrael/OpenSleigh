using Microsoft.Extensions.DependencyInjection;
using System;

namespace OpenSleigh.Core.DependencyInjection
{
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
                    foreach(var t in sagaTypes)
                        resolver.Register(t);
                    
                    return resolver;
                })
                .AddSingleton<IMessageContextFactory, DefaultMessageContextFactory>()
                .AddSingleton<IMessageBus, DefaultMessageBus>()
                .AddSingleton<IMessageProcessor, MessageProcessor>();

            var builder = new BusConfigurator(services, stateTypeResolver);
            configure?.Invoke(builder);

            services.AddHostedService<SagasBackgroundService>();

            return services;
        }
    }

}