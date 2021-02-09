using System.Diagnostics.CodeAnalysis;
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
            var messageTypes = SagaUtils<TS, TD>.GetHandledMessageTypes();
            foreach (var messageType in messageTypes)
                sagaConfigurator.Services.AddBusSubscriber(
                    typeof(ServiceBusSubscriber<>).MakeGenericType(messageType));

            return sagaConfigurator;
        }

    }
}