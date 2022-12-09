using Microsoft.Extensions.Logging;
using OpenSleigh.Messaging;
using OpenSleigh.Utils;

namespace OpenSleigh.E2ETests
{
    public record StartMultipleSagas() : IMessage { }

    public record ProcessSaga() : IMessage { }

    public record SagaCompleted() : IMessage { }

    public record MySagaState
    {
        public int Foo = 42;
        public string Bar = "71";
    };

    public class SagaWithState :
        Saga<MySagaState>,
        IStartedBy<StartMultipleSagas>,
        IHandleMessage<ProcessSaga>,
        IHandleMessage<SagaCompleted>
    {
        private readonly ILogger<SagaWithState> _logger;
        private readonly Action<IMessageContext<SagaCompleted>> _onCompleted;
        public SagaWithState(
            Action<IMessageContext<SagaCompleted>> onCompleted,
            ILogger<SagaWithState> logger, 
            ISagaExecutionContext<MySagaState> context,
            ISerializer serializer) : base(context, serializer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        }

        public ValueTask HandleAsync(IMessageContext<StartMultipleSagas> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("starting saga with state '{InstanceId}'...", this.Context.InstanceId);

            var message = new ProcessSaga();
            this.Publish(message);

            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<ProcessSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("processing saga with state '{InstanceId}'...", this.Context.InstanceId);

            var message = new SagaCompleted();
            this.Publish(message);

            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<SagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.Context.MarkAsCompleted();

            _logger.LogInformation("saga with state '{InstanceId}' completed!", this.Context.InstanceId);

            _onCompleted(context);

            return ValueTask.CompletedTask;
        }
    }
}
