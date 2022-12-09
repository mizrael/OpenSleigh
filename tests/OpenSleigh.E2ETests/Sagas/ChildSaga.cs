using OpenSleigh.Messaging;
using OpenSleigh.Utils;

namespace OpenSleigh.E2ETests
{
    public record StartChildSaga : IMessage;

    public record ProcessChildSaga : IMessage;

    public record ChildSagaCompleted : IMessage;

    public class ChildSaga :
        Saga,
        IStartedBy<StartChildSaga>,
        IHandleMessage<ProcessChildSaga>
    {
        public ChildSaga(
            ISagaExecutionContext context,
            ISerializer serializer) : base(context, serializer)
        {
        }

        public async ValueTask HandleAsync(IMessageContext<StartChildSaga> context, CancellationToken cancellationToken = default)
        {
            var message = new ProcessChildSaga();
            this.Publish(message);
        }

        public async ValueTask HandleAsync(IMessageContext<ProcessChildSaga> context, CancellationToken cancellationToken = default)
        {
            this.Context.MarkAsCompleted();
            
            var completedEvent = new ChildSagaCompleted();
            this.Publish(completedEvent);
        }
    }
}