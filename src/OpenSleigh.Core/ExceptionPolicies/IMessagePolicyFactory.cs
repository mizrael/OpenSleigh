using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public interface IMessagePolicyFactory<TS, TM>
        where TS : ISaga
        where TM : IMessage
    {
        IPolicy Create();
    }
}