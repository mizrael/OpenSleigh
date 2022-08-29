using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {        
        public static IServiceCollection AddBusSubscriber(this IServiceCollection services, Type subscriberType)
        {
            if (!services.Any(s => s.ImplementationType == subscriberType))
                services.AddSingleton(typeof(ISubscriber), subscriberType);
            return services;
        }
    }
}