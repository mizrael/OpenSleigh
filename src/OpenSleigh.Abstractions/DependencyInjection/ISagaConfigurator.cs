using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.ExceptionPolicies;
using OpenSleigh.Core.Messaging;
using System;

namespace OpenSleigh.Core.DependencyInjection
{
    public interface ISagaConfigurator<TS, in TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        IServiceCollection Services { get; }
        ISagaConfigurator<TS, TD> UseStateFactory<TM>(Func<TM, TD> stateFactory)
            where TM : IMessage;

        ISagaConfigurator<TS, TD> UseRetryPolicy<TM>(Action<IRetryPolicyBuilder> builderAction)
            where TM : IMessage;
    }
}