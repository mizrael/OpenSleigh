using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.BackgroundServices;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOpenSleigh(this IServiceCollection services, Action<IBusConfigurator> configure = null)
        {
            var systemInfo = SystemInfo.New();

            var typeResolver = new TypeResolver();

            RegisterAllMessages(typeResolver);

            var sagaTypeResolver = new SagaTypeResolver(typeResolver);

            services.AddTransient<IMessageBus, DefaultMessageBus>()
                .AddSingleton(systemInfo)
                .AddSingleton<ISystemInfo>(systemInfo)
                .AddSingleton<ISagaTypeResolver>(sagaTypeResolver)
                .AddSingleton<ISagasRunner, SagasRunner>()
                .AddSingleton<ISagaRunnersFactory, SagaRunnersFactory>()
                .AddSingleton<ITypesCache, TypesCache>()
                .AddSingleton<ITypeResolver>(typeResolver)

                .AddSingleton<ITransportSerializer, JsonSerializer>()
                .AddSingleton<IPersistenceSerializer, JsonSerializer>()

                .AddSingleton<IMessageHandlersResolver, DefaultMessageHandlersResolver>()
                .AddSingleton<IMessageHandlersRunner, DefaultMessageHandlersRunner>()
                .AddSingleton<IMessageContextFactory, DefaultMessageContextFactory>()

                .AddSingleton<IMessageProcessor, MessageProcessor>()
                .AddHostedService<SubscribersBackgroundService>()

                .AddTransient<IOutboxProcessor, OutboxProcessor>()
                .AddSingleton(OutboxProcessorOptions.Default)
                .AddHostedService<OutboxBackgroundService>()

                .AddTransient<IOutboxCleaner, OutboxCleaner>()
                .AddSingleton(OutboxCleanerOptions.Default)
                .AddHostedService<OutboxCleanerBackgroundService>()
                ;

            var builder = new BusConfigurator(services, sagaTypeResolver, typeResolver, systemInfo);
            configure?.Invoke(builder);

            return services;
        }

        /// <summary>
        /// caches all the message types. This will allow all the classes referencing ITypeResolver to work properly.
        /// </summary>
        /// <param name="typeResolver"></param>
        private static void RegisterAllMessages(ITypeResolver typeResolver)
        {
            Console.WriteLine("preloading all message types...");

            var messageType = typeof(IMessage);
            
            // Assemblies are lazy loaded so using AppDomain.GetAssemblies is not reliable.            
            var currAssembly = Assembly.GetEntryAssembly();
            var visited = new HashSet<string>();            
            var queue = new Queue<Assembly>();
            queue.Enqueue(currAssembly);
            while (queue.Any())
            {
                var assembly = queue.Dequeue();
                visited.Add(assembly.FullName);

                var assemblyTypes = assembly.GetTypes();
                foreach (var type in assemblyTypes)
                {
                    if (messageType.IsAssignableFrom(type))
                        typeResolver.Register(type);
                }

                var references = assembly.GetReferencedAssemblies();
                foreach(var reference in references)
                {
                    if (visited.Contains(reference.FullName))
                        continue;
                    queue.Enqueue(Assembly.Load(reference));
                }
            }

            Console.WriteLine("preloading all message types completed!");
        }
        
        public static IServiceCollection AddBusSubscriber(this IServiceCollection services, Type subscriberType)
        {
            if (!services.Any(s => s.ImplementationType == subscriberType))
                services.AddSingleton(typeof(ISubscriber), subscriberType);
            return services;
        }
    }

}