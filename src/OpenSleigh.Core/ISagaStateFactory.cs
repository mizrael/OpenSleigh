using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ISagaStateFactory<out TD>
        where TD : SagaState
    {
        TD Create(IMessage message);
    }
}