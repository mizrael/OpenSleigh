using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Samples.Sample1
{
    public record MySagaState
    {
        public int Foo = 42;
        public string Bar = "71";
    };

    public class SagaWithState :
        Saga<MySagaState>,
        IStartedBy<StartSaga>,
        IHandleMessage<ProcessMySaga>,
        IHandleMessage<MySagaCompleted>
    {
        private readonly ILogger<SagaWithState> _logger;

        public SagaWithState(
            ILogger<SagaWithState> logger, 
            ISagaExecutionContext<MySagaState> context,
            ISerializer serializer) : base(context, serializer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValueTask HandleAsync(IMessageContext<StartSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("starting saga with state '{InstanceId}'...", this.Context.InstanceId);

            var message = new ProcessMySaga();
            this.Publish(message);

            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<ProcessMySaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("processing saga with state '{InstanceId}'...", this.Context.InstanceId);

            var message = new MySagaCompleted();
            this.Publish(message);

            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(IMessageContext<MySagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.Context.MarkAsCompleted();

            _logger.LogInformation("saga with state '{InstanceId}' completed!", this.Context.InstanceId);

            return ValueTask.CompletedTask;
        }
    }
}
