using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public interface ISagaPolicyFactory<TS>
        where TS : ISaga
    {
        IPolicy Create<TM>() where TM : IMessage;
    }
}