using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record StartOnboarding(string CorrelationId, string Customer) : IMessage, IHasCorrelationId;

public record VerifyIdentity : IMessage;

public record ActivateAccount : IMessage;

public class OnboardingState
{
    public string Customer { get; set; } = string.Empty;

    /// <summary>the messages handled so far, by the name of their type, in the order they were handled.</summary>
    public List<string> Steps { get; set; } = [];
}

/// <summary>
/// three steps in a row, each started by the message the previous one published, and each adding to the state.
/// </summary>
public class OnboardingSaga(ISagaInstance<OnboardingState> context) :
    Saga<OnboardingState>(context),
    IStartedBy<StartOnboarding>,
    IHandleMessage<VerifyIdentity>,
    IHandleMessage<ActivateAccount>
{
    public ValueTask HandleAsync(IMessageContext<StartOnboarding> context, CancellationToken cancellationToken = default)
    {
        Context.State.Customer = context.Message.Customer;
        Context.State.Steps.Add(nameof(StartOnboarding));
        Publish(new VerifyIdentity());

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<VerifyIdentity> context, CancellationToken cancellationToken = default)
    {
        Context.State.Steps.Add(nameof(VerifyIdentity));
        Publish(new ActivateAccount());

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<ActivateAccount> context, CancellationToken cancellationToken = default)
    {
        Context.State.Steps.Add(nameof(ActivateAccount));
        Context.MarkAsCompleted();

        return ValueTask.CompletedTask;
    }
}
