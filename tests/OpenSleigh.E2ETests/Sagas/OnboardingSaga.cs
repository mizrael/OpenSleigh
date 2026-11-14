using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record StartOnboarding(string CorrelationId, string Customer) : IMessage, IHasCorrelationId;

public record VerifyIdentity : IMessage;

public record ActivateAccount : IMessage;

public record OnboardingState
{
    public string Customer { get; set; } = string.Empty;

    /// <summary>the messages handled so far, by the name of their type, in the order they were handled.</summary>
    public List<string> Steps { get; set; } = [];
}

/// <summary>tells the scenario which host handled a step: every host has its own.</summary>
public delegate void OnboardingStepHandled(string step);

/// <summary>
/// three steps in a row, each started by the message the previous one published, and each adding to the state.
/// </summary>
public class OnboardingSaga :
    Saga<OnboardingState>,
    IStartedBy<StartOnboarding>,
    IHandleMessage<VerifyIdentity>,
    IHandleMessage<ActivateAccount>
{
    private readonly OnboardingStepHandled _onStepHandled;

    public OnboardingSaga(OnboardingStepHandled onStepHandled, ISagaInstance<OnboardingState> context) : base(context)
    {
        _onStepHandled = onStepHandled ?? throw new ArgumentNullException(nameof(onStepHandled));
    }

    public ValueTask HandleAsync(IMessageContext<StartOnboarding> context, CancellationToken cancellationToken = default)
    {
        Context.State.Customer = context.Message.Customer;
        Publish(new VerifyIdentity());

        return StepHandled(nameof(StartOnboarding));
    }

    public ValueTask HandleAsync(IMessageContext<VerifyIdentity> context, CancellationToken cancellationToken = default)
    {
        Publish(new ActivateAccount());

        return StepHandled(nameof(VerifyIdentity));
    }

    public ValueTask HandleAsync(IMessageContext<ActivateAccount> context, CancellationToken cancellationToken = default)
    {
        Context.MarkAsCompleted();

        return StepHandled(nameof(ActivateAccount));
    }

    private ValueTask StepHandled(string step)
    {
        Context.State.Steps.Add(step);
        _onStepHandled(step);

        return ValueTask.CompletedTask;
    }
}
