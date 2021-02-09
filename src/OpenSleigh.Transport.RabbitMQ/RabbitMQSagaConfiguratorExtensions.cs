using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
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
            var messageTypes = SagaUtils<TS, TD>.GetHandledMessageTypes();
            foreach(var messageType in messageTypes)
                sagaConfigurator.Services.AddSingleton(typeof(ISubscriber),
                    typeof(RabbitSubscriber<>).MakeGenericType(messageType));
            
            return sagaConfigurator;
        }
    }
}