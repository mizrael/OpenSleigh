using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ISagaStateFactory<out TD>
        where TD : SagaState
    {
        //TODO: this should probably be a generic method
        TD Create(IMessage message);
    }
}