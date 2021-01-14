using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IRabbitBusConfigurationBuilder
    {
        void UseMessageNamingPolicy<TM>(QueueReferencesPolicy<TM> policy) where TM : IMessage;
    }

    [ExcludeFromCodeCoverage]
    internal class DefaultRabbitBusConfigurationBuilder : IRabbitBusConfigurationBuilder
    {
        private readonly IBusConfigurator _busConfigurator;

        public DefaultRabbitBusConfigurationBuilder(IBusConfigurator busConfigurator)
        {
            _busConfigurator = busConfigurator;
        }

        public void UseMessageNamingPolicy<TM>(QueueReferencesPolicy<TM> policy) where TM : IMessage
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            
            _busConfigurator.Services.AddSingleton(policy);
        }
    }

}