using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public class MessagePolicyFactory<TS, TD, TM> :
        IMessagePolicyFactory<TS, TD, TM>
        where TD : SagaState
        where TS : Saga<TD>
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