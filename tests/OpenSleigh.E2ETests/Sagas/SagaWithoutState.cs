using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests;

public class SagaWithoutState :
    Saga,
    IStartedBy<StartMultipleSagas>,
    IHandleMessage<ProcessSaga>,
    IHandleMessage<SagaCompleted>
{
    private readonly ILogger<SagaWithoutState> _logger;
    private readonly Action<IMessageContext<SagaCompleted>> _onCompleted;

    public SagaWithoutState(
        Action<IMessageContext<SagaCompleted>> onCompleted,
        ILogger<SagaWithoutState> logger,
        ISagaInstance  context) : base(context)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
    }

    public ValueTask HandleAsync(IMessageContext<StartMultipleSagas> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("starting saga without state '{InstanceId}'...", this.Context.InstanceId);

        var message = new ProcessSaga();
        this.Publish(message);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<ProcessSaga> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("processing saga without state '{InstanceId}'...", this.Context.InstanceId);

        var message = new SagaCompleted();
        this.Publish(message);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<SagaCompleted> context, CancellationToken cancellationToken = default)
    {
        this.Context.MarkAsCompleted();

        _logger.LogInformation("saga without state '{InstanceId}' completed!", this.Context.InstanceId);

        _onCompleted(context);

        return ValueTask.CompletedTask;
    }
}
