using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record StartSubscription(string CorrelationId, int Cycles, decimal Fee) : IMessage, IHasCorrelationId;

public record BillCycle(int Cycle) : IMessage;

public record SubscriptionState
{
    public int Cycles { get; set; }
    public decimal Fee { get; set; }
    public int CyclesBilled { get; set; }
    public decimal TotalBilled { get; set; }

    /// <summary>how many cycles were billed out of turn: twice, or before the one that comes first.</summary>
    public int CyclesOutOfTurn { get; set; }
}

/// <summary>
/// a saga that keeps itself going for many steps: every billing cycle publishes the next one, until the last.
/// Each step builds on what the previous ones left in the state.
/// </summary>
public class SubscriptionSaga(ISagaInstance<SubscriptionState> context) :
    Saga<SubscriptionState>(context),
    IStartedBy<StartSubscription>,
    IHandleMessage<BillCycle>
{
    private const int FirstCycle = 1;

    /// <summary>what a subscription has billed once its cycles are over: cycle n costs n times the fee.</summary>
    public static decimal TotalOf(int cycles, decimal fee) => fee * cycles * (cycles + 1) / 2;

    public ValueTask HandleAsync(IMessageContext<StartSubscription> context, CancellationToken cancellationToken = default)
    {
        Context.State.Cycles = context.Message.Cycles;
        Context.State.Fee = context.Message.Fee;

        Publish(new BillCycle(FirstCycle));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<BillCycle> context, CancellationToken cancellationToken = default)
    {
        var cycle = context.Message.Cycle;
        if (cycle != Context.State.CyclesBilled + 1)
            Context.State.CyclesOutOfTurn++;

        Context.State.CyclesBilled++;
        Context.State.TotalBilled += Context.State.Fee * cycle;

        if (cycle < Context.State.Cycles)
            Publish(new BillCycle(cycle + 1));
        else
            Context.MarkAsCompleted();

        return ValueTask.CompletedTask;
    }
}
