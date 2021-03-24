using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.Kafka
{
    [ExcludeFromCodeCoverage]
    public static class KafkaSagaConfiguratorExtensions
    {
        public static ISagaConfigurator<TS, TD> UseKafkaTransport<TS, TD>(this ISagaConfigurator<TS, TD> sagaConfigurator)
            where TS : Saga<TD>
            where TD : SagaState
        {
            var messageTypes = SagaUtils<TS, TD>.GetHandledMessageTypes();
            foreach (var messageType in messageTypes)
                sagaConfigurator.Services.AddBusSubscriber(
                    typeof(KafkaSubscriber<>).MakeGenericType(messageType));

            return sagaConfigurator;
        }
    }
}