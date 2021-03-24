using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Transport.Kafka
{
    public interface IKafkaBusConfigurationBuilder
    {
        void UseMessageNamingPolicy<TM>(QueueReferencesPolicy<TM> policy) where TM : IMessage;
    }


    [ExcludeFromCodeCoverage]
    internal class DefaultKafkaBusConfigurationBuilder : IKafkaBusConfigurationBuilder
    {
        private readonly IBusConfigurator _busConfigurator;

        public DefaultKafkaBusConfigurationBuilder(IBusConfigurator busConfigurator)
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