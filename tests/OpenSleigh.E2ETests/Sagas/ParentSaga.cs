using Microsoft.Extensions.Logging;
using OpenSleigh.Messaging;
using OpenSleigh.Utils;
using System.Runtime.Serialization;

namespace OpenSleigh.E2ETests
{
    public record StartParentSaga : IMessage;

    public record ProcessParentSaga : IMessage;

    public record ParentSagaCompleted : IMessage;

    public class ParentSaga :
        Saga,
        IStartedBy<StartParentSaga>,
        IHandleMessage<ProcessParentSaga>,
        IHandleMessage<ChildSagaCompleted>,
        IHandleMessage<ParentSagaCompleted>
    {
        private readonly Action<IMessageContext<ParentSagaCompleted>> _onCompleted;        
        private readonly ILogger<ParentSaga> _logger;
        
        public ParentSaga(
            Action<IMessageContext<ParentSagaCompleted>> onCompleted, 
            ILogger<ParentSaga> logger, 
            ISagaExecutionContext context,
            ISerializer serializer) : base(context, serializer)
        {
            _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            _logger = logger;
        }

        public async ValueTask HandleAsync(IMessageContext<StartParentSaga> context, CancellationToken cancellationToken = default)
        {
            var message = new ProcessParentSaga();
            this.Publish(message);
        }

        public async ValueTask HandleAsync(IMessageContext<ProcessParentSaga> context, CancellationToken cancellationToken = default)
        {
            var message = new StartChildSaga();
            this.Publish(message);
        }

        public async ValueTask HandleAsync(IMessageContext<ChildSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            var message = new ParentSagaCompleted();
            this.Publish(message);
        }

        public ValueTask HandleAsync(IMessageContext<ParentSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"completing Parent Saga '{this.Context.InstanceId}'");

            this.Context.MarkAsCompleted();

            _onCompleted?.Invoke(context);

            return ValueTask.CompletedTask;
        }
    }
}