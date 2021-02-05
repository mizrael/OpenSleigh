using System;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public interface ISagaPolicyFactory<TS, TD>
        where TD : SagaState
        where TS : Saga<TD>
    {
        IPolicy Create<TM>() where TM : IMessage;
    }

    internal class DefaultSagaPolicyFactory<TS, TD> : ISagaPolicyFactory<TS, TD>
        where TD : SagaState
        where TS : Saga<TD>
    {
        private readonly IServiceProvider _serviceProvider;

        public DefaultSagaPolicyFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IPolicy Create<TM>() where TM : IMessage
        {
            var factory = _serviceProvider.GetService<IMessagePolicyFactory<TS, TD, TM>>();
            return factory?.Create();
        }
    }
}