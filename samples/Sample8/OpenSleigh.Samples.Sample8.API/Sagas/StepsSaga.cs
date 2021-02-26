using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample8.API.Sagas
{
    public class StepsSagaState : SagaState
    {
        public StepsSagaState(Guid id) : base(id)
        {
        }
        
        public int TotalSteps { get; set; }
        public int CurrentStep { get; set; }
    }

    public record StartSaga(int StepsCount, Guid Id, Guid CorrelationId) : IMessage;
    public record ProcessNextStep(Guid Id, Guid CorrelationId) : IMessage;
    public record SagaCompleted(Guid Id, Guid CorrelationId) : IEvent;

    public class StepsSaga : Saga<StepsSagaState>,
        IStartedBy<StartSaga>,
        IHandleMessage<ProcessNextStep>,
        IHandleMessage<SagaCompleted>
    {
        private readonly ILogger<StepsSaga> _logger;

        public StepsSaga(ILogger<StepsSaga> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(IMessageContext<StartSaga> context, CancellationToken cancellationToken = default)
        {
            this.State.TotalSteps = context.Message.StepsCount;
            this.State.CurrentStep = 0;
            
            _logger.LogInformation($"starting saga {context.Message.CorrelationId} with {State.TotalSteps} total steps...");

            await this.Bus.PublishAsync(
                new ProcessNextStep(Guid.NewGuid(), context.Message.CorrelationId),
                cancellationToken);
        }

        public async Task HandleAsync(IMessageContext<ProcessNextStep> context,
            CancellationToken cancellationToken = default)
        {
            this.State.CurrentStep++;

            if (State.CurrentStep > State.TotalSteps)
            {
                await this.Bus.PublishAsync(
                    new SagaCompleted(Guid.NewGuid(), context.Message.CorrelationId),
                    cancellationToken);
                return;
            }

            _logger.LogInformation($"processing step {State.CurrentStep}/{State.TotalSteps} on saga {context.Message.CorrelationId} ...");

            await Task.Delay(1000, cancellationToken);

            await this.Bus.PublishAsync(
                new ProcessNextStep(Guid.NewGuid(), context.Message.CorrelationId),
                cancellationToken);
        }

        public Task HandleAsync(IMessageContext<SagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted(); 
            _logger.LogInformation($"saga {context.Message.CorrelationId} completed!");
            return Task.CompletedTask;
        }
    }
}
