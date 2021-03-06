using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.AzureServiceBus
{
    [ExcludeFromCodeCoverage]
    public static class AzureServiceBusSagaConfiguratorExtensions
    {
        public static ISagaConfigurator<TS, TD> UseAzureServiceBusTransport<TS, TD>(this ISagaConfigurator<TS, TD> sagaConfigurator)
            where TS : Saga<TD>
            where TD : SagaState
        {
            var messageTypes = typeof(TS).GetHandledMessageTypes();
            foreach (var messageType in messageTypes)
            {
                sagaConfigurator.Services.AddBusSubscriber(
                    typeof(ServiceBusSubscriber<>).MakeGenericType(messageType));
                sagaConfigurator.Services.AddSingleton(typeof(IInfrastructureCreator),
                    typeof(AzureServiceBusInfrastructureCreator<>).MakeGenericType(messageType));
            }
            
            return sagaConfigurator;
        }

    }
}