using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.DependencyInjection
{
    public interface ISagaStateFactory<out TD>
        where TD : SagaState
    {
        TD Create(IMessage message);
    }
}