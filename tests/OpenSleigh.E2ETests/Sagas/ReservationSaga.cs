using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record ReserveSeat(string CorrelationId) : IMessage, IHasCorrelationId;

public record ConfirmReservation(string CorrelationId) : IMessage, IHasCorrelationId;

public record CancelReservation(string CorrelationId) : IMessage, IHasCorrelationId;

/// <summary>
/// a reservation that is over once it is confirmed. A cancellation can be late, or be for a reservation nobody made.
/// </summary>
public class ReservationSaga(ISagaInstance context, HandlerRuns runs) :
    Saga(context),
    IStartedBy<ReserveSeat>,
    IHandleMessage<ConfirmReservation>,
    IHandleMessage<CancelReservation>
{
    public ValueTask HandleAsync(IMessageContext<ReserveSeat> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<ConfirmReservation> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Context.MarkAsCompleted();
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<CancelReservation> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        return ValueTask.CompletedTask;
    }
}
