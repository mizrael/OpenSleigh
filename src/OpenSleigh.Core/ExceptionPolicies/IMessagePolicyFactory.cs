using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public interface IMessagePolicyFactory<TS, TD, TM>
        where TD : SagaState
        where TS : Saga<TD>
        where TM : IMessage
    {
        IPolicy Create();
    }
}