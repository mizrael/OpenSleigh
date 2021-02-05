using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public class MessagePolicyFactory<TS, TM> :
        IMessagePolicyFactory<TS, TM>
        where TS : ISaga
        where TM : IMessage
    {
        private readonly IPolicyBuilder _builder;

        public MessagePolicyFactory(IPolicyBuilder builder)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public IPolicy Create() => _builder.Build();
    }
}