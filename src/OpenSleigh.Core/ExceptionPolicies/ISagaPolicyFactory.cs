using System;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public interface ISagaPolicyFactory<TS>
        where TS : ISaga
    {
        IPolicy Create<TM>() where TM : IMessage;
    }

    internal class DefaultSagaPolicyFactory<TS> : ISagaPolicyFactory<TS>
        where TS : ISaga
    {
        private readonly IServiceProvider _serviceProvider;

        public DefaultSagaPolicyFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IPolicy Create<TM>() where TM : IMessage
        {
            var factory = _serviceProvider.GetService<IMessagePolicyFactory<TS, TM>>();
            return factory?.Create();
        }
    }
}