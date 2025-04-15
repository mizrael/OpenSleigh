using Microsoft.AspNetCore.SignalR;
using OpenSleigh.Samples.Blazor.Hubs;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.Blazor.Sagas;

public record StepsSagaState
{
    public string ClientId { get; set; }
    public int TotalSteps { get; set; }
    public int CurrentStep { get; set; }
}

public record StartStepsSaga(int StepsCount, string ClientId) : IMessage;
public record ProcessNextStep : IMessage;
public record SagaCompleted : IMessage;

public class StepsSaga : Saga<StepsSagaState>,
    IStartedBy<StartStepsSaga>,
    IHandleMessage<ProcessNextStep>,
    IHandleMessage<SagaCompleted>
{
    private readonly IHubContext<SagaHub> _hubContext;
    private readonly ILogger<StepsSaga> _logger;

    public StepsSaga(
        ILogger<StepsSaga> logger, 
        IHubContext<SagaHub> hubContext, 
        ISagaExecutionContext<StepsSagaState> context) : base(context)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    private async ValueTask SendNotification(string text, bool done = false)
    {
        _logger.LogInformation(text);

        var notification = new Notification(text, DateTimeOffset.UtcNow);

        var client = _hubContext.Clients.Client(this.Context.State.ClientId);        
        await client.SendAsync("Notification", notification, done);
    }

    public async ValueTask HandleAsync(IMessageContext<StartStepsSaga> context, CancellationToken cancellationToken = default)
    {
        this.Context.State.ClientId = context.Message.ClientId;
        this.Context.State.TotalSteps = context.Message.StepsCount;
        this.Context.State.CurrentStep = 0;

        await SendNotification($"starting saga {context.CorrelationId} with {this.Context.State.TotalSteps} total steps...");
        
        this.Publish(new ProcessNextStep());
    }

    public async ValueTask HandleAsync(IMessageContext<ProcessNextStep> context,
        CancellationToken cancellationToken = default)
    {
        this.Context.State.CurrentStep++;

        if (this.Context.State.CurrentStep > this.Context.State.TotalSteps)
        {
            this.Publish(new SagaCompleted());
            return;
        }

        await SendNotification($"processing step {this.Context.State.CurrentStep}/{this.Context.State.TotalSteps} on saga {context.CorrelationId} ...");

        await Task.Delay(250, cancellationToken);

        this.Publish(new ProcessNextStep());
    }

    public async ValueTask HandleAsync(IMessageContext<SagaCompleted> context, CancellationToken cancellationToken = default)
    {
        this.Context.MarkAsCompleted();
        await SendNotification($"saga {context.CorrelationId} completed!", true);
    }
}
