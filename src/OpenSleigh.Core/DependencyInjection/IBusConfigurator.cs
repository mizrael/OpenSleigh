using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.DependencyInjection
{
    public interface IBusConfigurator
    {
        /// <summary>
        /// Sets a client application as publish-only. A "publish-only" application will only
        /// take care of dispatching new messages but won't be able to consume any.
        /// </summary>
        IBusConfigurator SetPublishOnly(bool value = true);

        IBusConfigurator WithOutboxProcessorOptions(OutboxProcessorOptions options);
        IBusConfigurator WithOutboxCleanerOptions(OutboxCleanerOptions options);

        /// <summary>
        /// registers all the message handlers contained in the input assemblies 
        /// for the specific Message.
        /// Sagas will be skipped, use AddSaga() instead.
        /// </summary>
        IMessageHandlerConfigurator<TM> AddMessageHandlers<TM>(IEnumerable<Assembly> sourceAssemblies)       
            where TM : IMessage;                  
        
        ISagaConfigurator<TS, TD> AddSaga<TS, TD>()
            where TS : Saga<TD>
            where TD : SagaState;

        IServiceCollection Services { get; }
    }
}