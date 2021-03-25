using Microsoft.Extensions.DependencyInjection;
using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    internal class DefaultSagaFactory<TS, TD> : ISagaFactory<TS, TD>
        where TD : SagaState
        where TS : Saga<TD>
    {
        private readonly IServiceProvider _serviceProvider;

        public DefaultSagaFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public TS Create(TD state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            
            var saga = _serviceProvider.GetRequiredService<TS>();

            saga.SetState(state);
            
            return saga;
        }
    }
}