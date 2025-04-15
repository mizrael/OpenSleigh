using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.Sample1;

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
        ISagaExecutionContext<MySagaState> context) : base(context)
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
        _logger.LogInformation("state: Foo = {Foo}, Bar = {Bar}", this.Context.State.Foo, this.Context.State.Bar);

        this.Context.State.Foo = 100;
        this.Context.State.Bar = "lorem ipsum";

        var message = new MySagaCompleted();
        this.Publish(message);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<MySagaCompleted> context, CancellationToken cancellationToken = default)
    {
        this.Context.MarkAsCompleted();

        _logger.LogInformation("state: Foo = {Foo}, Bar = {Bar}", this.Context.State.Foo, this.Context.State.Bar);

        _logger.LogInformation("saga with state '{InstanceId}' completed!", this.Context.InstanceId);

        return ValueTask.CompletedTask;
    }
}
