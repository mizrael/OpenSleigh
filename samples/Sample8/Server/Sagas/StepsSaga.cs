using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample8.Server.Hubs;

namespace OpenSleigh.Samples.Sample8.Server.Sagas
{
    public record StepsSagaState : SagaState
    {
        public StepsSagaState(Guid id) : base(id)
        {
        }
        
        public string ClientId { get; set; }
        public int TotalSteps { get; set; }
        public int CurrentStep { get; set; }
    }

    public record StartSaga(int StepsCount, string ClientId, Guid Id, Guid CorrelationId) : IMessage;
    public record ProcessNextStep(Guid Id, Guid CorrelationId) : IMessage;
    public record SagaCompleted(Guid Id, Guid CorrelationId) : IEvent;

    public class StepsSaga : Saga<StepsSagaState>,
        IStartedBy<StartSaga>,
        IHandleMessage<ProcessNextStep>,
        IHandleMessage<SagaCompleted>
    {
        private readonly IHubContext<SagaHub> _hubContext;
        private readonly ILogger<StepsSaga> _logger;

        public StepsSaga(ILogger<StepsSaga> logger, IHubContext<SagaHub> hubContext, StepsSagaState state) : base(state)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        }

        private async Task SendNotification(string text, bool done = false)
        {
            _logger.LogInformation(text);

            var client = _hubContext.Clients.Client(this.State.ClientId);
            await client?.SendAsync("Notification", text, done);
        }
        
        public async Task HandleAsync(IMessageContext<StartSaga> context, CancellationToken cancellationToken = default)
        {
            this.State.ClientId = context.Message.ClientId;
            this.State.TotalSteps = context.Message.StepsCount;
            this.State.CurrentStep = 0;

            await SendNotification($"starting saga {context.Message.CorrelationId} with {State.TotalSteps} total steps...");
                
            this.Publish(new ProcessNextStep(Guid.NewGuid(), context.Message.CorrelationId));
        }

        public async Task HandleAsync(IMessageContext<ProcessNextStep> context,
            CancellationToken cancellationToken = default)
        {
            this.State.CurrentStep++;

            if (State.CurrentStep > State.TotalSteps)
            {
                this.Publish(new SagaCompleted(Guid.NewGuid(), context.Message.CorrelationId));
                return;
            }

            await SendNotification($"processing step {State.CurrentStep}/{State.TotalSteps} on saga {context.Message.CorrelationId} ...");

            await Task.Delay(250, cancellationToken);

            this.Publish(new ProcessNextStep(Guid.NewGuid(), context.Message.CorrelationId));
        }

        public async Task HandleAsync(IMessageContext<SagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.State.MarkAsCompleted();
            await SendNotification($"saga {context.Message.CorrelationId} completed!", true);
        }
    }
}
