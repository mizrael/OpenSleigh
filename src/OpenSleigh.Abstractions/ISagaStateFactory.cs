using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ISagaStateFactory<in TM, out TD> : ISagaStateFactory<TD>
        where TM : IMessage
        where TD : SagaState
    {
        TD Create(TM message);
    }

    public interface ISagaStateFactory<out TD> where TD : SagaState
    {
        TD Create(IMessage message);
    }
}