using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.BackgroundServices
{
    public class SagasBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SagasBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var typesCache = scope.ServiceProvider.GetRequiredService<ITypesCache>();

            var typeResolver = scope.ServiceProvider.GetRequiredService<ISagaTypeResolver>();
            var messageTypes = typeResolver.GetMessageTypes();

            var subscriberRawType = typeof(ISubscriber<>);
            var tasks = new List<Task>();

            foreach (var messageType in messageTypes)
            {
                var subscriberType = typesCache.GetGeneric(subscriberRawType, messageType);

                dynamic subscriber = scope.ServiceProvider.GetService(subscriberType);
                if (null == subscriber)
                    continue;

                var startMethod = typesCache.GetMethod(subscriberType, nameof(ISubscriber<IMessage>.StartAsync));
                var t = (Task)startMethod.Invoke(subscriber, new[] { (object)stoppingToken });
                tasks.Add(t);
            }

            await Task.WhenAll(tasks.ToArray());
        }
    }
}
