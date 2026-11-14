using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests;

/// <summary>
/// started by the same message as <see cref="SimpleSaga"/>: the saga of another service that listens to it.
/// </summary>
public class OtherSimpleSaga(Action<IMessageContext<StartSimpleSaga>> onStart, ISagaInstance context) :
    Saga(context),
    IStartedBy<StartSimpleSaga>
{
    public ValueTask HandleAsync(IMessageContext<StartSimpleSaga> context, CancellationToken cancellationToken = default)
    {
        onStart(context);
        return ValueTask.CompletedTask;
    }
}
