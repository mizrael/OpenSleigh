using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    public interface IAzureServiceBusConfigurationBuilder
    {
        void UseMessageNamingPolicy<TM>(QueueReferencesPolicy<TM> policy) where TM : IMessage;
    }

    [ExcludeFromCodeCoverage]
    internal class DefaultAzureServiceBusConfigurationBuilder : IAzureServiceBusConfigurationBuilder
    {
        private readonly IBusConfigurator _busConfigurator;

        public DefaultAzureServiceBusConfigurationBuilder(IBusConfigurator busConfigurator)
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