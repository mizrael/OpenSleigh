using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.RabbitMQ
{
    [ExcludeFromCodeCoverage]
    public static class RabbitMQSagaConfiguratorExtensions
    {
        public static ISagaConfigurator<TS, TD> UseRabbitMQTransport<TS, TD>(this ISagaConfigurator<TS, TD> sagaConfigurator)
            where TS : Saga<TD>
            where TD : SagaState
        {
            var messageTypes = typeof(TS).GetHandledMessageTypes();
            foreach (var messageType in messageTypes)
                sagaConfigurator.Services.AddBusSubscriber(
                    typeof(RabbitSubscriber<>).MakeGenericType(messageType));
            
            return sagaConfigurator;
        }
    }
}