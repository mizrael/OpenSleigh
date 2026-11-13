using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests;

public record StartFailingSaga : IMessage;

/// <summary>
/// a saga whose only handler reports the attempt and fails, every time.
/// </summary>
public class FailingSaga(Action<IMessageContext<StartFailingSaga>> onAttempt, ISagaInstance context) :
    Saga(context),
    IStartedBy<StartFailingSaga>
{
    public ValueTask HandleAsync(IMessageContext<StartFailingSaga> context, CancellationToken cancellationToken = default)
    {
        onAttempt(context);
        throw new InvalidOperationException("this handler fails on purpose.");
    }
}
