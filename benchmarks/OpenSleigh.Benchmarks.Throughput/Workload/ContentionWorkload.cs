using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenSleigh.Benchmarks.Throughput.Workload;

// SPIKE: workloads where several messages reach one saga instance at the same time.

// ---- an order workflow: one orchestrator, four workers, four replies that arrive together ----

public sealed record StartOrder : IMessage, IHasCorrelationId
{
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed record ReserveStock : IMessage;
public sealed record ChargePayment : IMessage;
public sealed record BookShipping : IMessage;
public sealed record NotifyCustomer : IMessage;

public sealed record StockReserved : IMessage;
public sealed record PaymentCharged : IMessage;
public sealed record ShippingBooked : IMessage;
public sealed record CustomerNotified : IMessage;

public sealed class OrderState
{
    public bool Stock { get; set; }
    public bool Payment { get; set; }
    public bool Shipping { get; set; }
    public bool Notified { get; set; }
    public int Replies { get; set; }
}

public sealed class OrderSaga : Saga<OrderState>,
    IStartedBy<StartOrder>,
    IHandleMessage<StockReserved>,
    IHandleMessage<PaymentCharged>,
    IHandleMessage<ShippingBooked>,
    IHandleMessage<CustomerNotified>
{
    /// <summary>handler runs for one order: the start, four workers, four replies.</summary>
    public const int HandlerRunsPerOrder = 9;

    private readonly ContentionProbe _probe;

    public OrderSaga(ISagaInstance<OrderState> context, ContentionProbe probe) : base(context)
    {
        _probe = probe;
    }

    public async ValueTask HandleAsync(IMessageContext<StartOrder> messageContext, CancellationToken cancellationToken = default)
    {
        await _probe.WorkAsync(cancellationToken);
        Publish(new ReserveStock());
        Publish(new ChargePayment());
        Publish(new BookShipping());
        Publish(new NotifyCustomer());
    }

    public ValueTask HandleAsync(IMessageContext<StockReserved> messageContext, CancellationToken cancellationToken = default)
        => ReplyAsync(messageContext.CorrelationId, state => state.Stock = true, cancellationToken);

    public ValueTask HandleAsync(IMessageContext<PaymentCharged> messageContext, CancellationToken cancellationToken = default)
        => ReplyAsync(messageContext.CorrelationId, state => state.Payment = true, cancellationToken);

    public ValueTask HandleAsync(IMessageContext<ShippingBooked> messageContext, CancellationToken cancellationToken = default)
        => ReplyAsync(messageContext.CorrelationId, state => state.Shipping = true, cancellationToken);

    public ValueTask HandleAsync(IMessageContext<CustomerNotified> messageContext, CancellationToken cancellationToken = default)
        => ReplyAsync(messageContext.CorrelationId, state => state.Notified = true, cancellationToken);

    private async ValueTask ReplyAsync(string correlationId, Action<OrderState> apply, CancellationToken cancellationToken)
    {
        await _probe.WorkAsync(cancellationToken);

        var state = Context.State;
        apply(state);
        state.Replies++;

        if (state.Stock && state.Payment && state.Shipping && state.Notified)
        {
            Context.MarkAsCompleted();
            _probe.Complete(correlationId, overcount: state.Replies - 4);
        }
    }
}

public abstract class WorkerSaga<TCommand, TReply> : Saga, IStartedBy<TCommand>
    where TCommand : IMessage
    where TReply : IMessage, new()
{
    private readonly ContentionProbe _probe;

    protected WorkerSaga(ISagaInstance context, ContentionProbe probe) : base(context)
    {
        _probe = probe;
    }

    public async ValueTask HandleAsync(IMessageContext<TCommand> messageContext, CancellationToken cancellationToken = default)
    {
        await _probe.WorkAsync(cancellationToken);
        Publish(new TReply());
        Context.MarkAsCompleted();
    }
}

public sealed class StockSaga(ISagaInstance context, ContentionProbe probe) : WorkerSaga<ReserveStock, StockReserved>(context, probe);
public sealed class PaymentSaga(ISagaInstance context, ContentionProbe probe) : WorkerSaga<ChargePayment, PaymentCharged>(context, probe);
public sealed class ShippingSaga(ISagaInstance context, ContentionProbe probe) : WorkerSaga<BookShipping, ShippingBooked>(context, probe);
public sealed class NotificationSaga(ISagaInstance context, ContentionProbe probe) : WorkerSaga<NotifyCustomer, CustomerNotified>(context, probe);

// ---- a burst: one saga instance receives many different messages at once ----

public sealed record StartBurst : IMessage, IHasCorrelationId
{
    public string CorrelationId { get; init; } = string.Empty;
    public int Items { get; init; }
}

public sealed record BurstItem : IMessage
{
    public int Index { get; init; }
}

public sealed class BurstState
{
    public int Expected { get; set; }
    public int Handled { get; set; }
    public List<int> Seen { get; set; } = [];
}

public sealed class BurstSaga : Saga<BurstState>, IStartedBy<StartBurst>, IHandleMessage<BurstItem>
{
    private readonly ContentionProbe _probe;

    public BurstSaga(ISagaInstance<BurstState> context, ContentionProbe probe) : base(context)
    {
        _probe = probe;
    }

    public async ValueTask HandleAsync(IMessageContext<StartBurst> messageContext, CancellationToken cancellationToken = default)
    {
        await _probe.WorkAsync(cancellationToken);

        Context.State.Expected = messageContext.Message.Items;
        for (var index = 0; index < messageContext.Message.Items; index++)
            Publish(new BurstItem { Index = index });
    }

    public async ValueTask HandleAsync(IMessageContext<BurstItem> messageContext, CancellationToken cancellationToken = default)
    {
        await _probe.WorkAsync(cancellationToken);

        var state = Context.State;
        state.Handled++;
        if (!state.Seen.Contains(messageContext.Message.Index))
            state.Seen.Add(messageContext.Message.Index);

        if (state.Seen.Count >= state.Expected)
        {
            Context.MarkAsCompleted();
            _probe.Complete(messageContext.CorrelationId, overcount: state.Handled - state.Expected);
        }
    }
}

/// <summary>
/// Shared by every host of a scenario: counts the handlers that ran, and when each unit of work started and completed.
/// </summary>
public sealed class ContentionProbe
{
    private readonly ConcurrentDictionary<string, long> _started = new();
    private readonly ConcurrentDictionary<string, double> _latencies = new();
    private long _handlerRuns;
    private long _overcount;
    private long _last;

    public TimeSpan HandlerDelay { get; set; } = TimeSpan.Zero;

    public long HandlerRuns => Interlocked.Read(ref _handlerRuns);

    /// <summary>changes applied to a saga's state more often than messages were sent to it: must stay zero.</summary>
    public long Overcount => Interlocked.Read(ref _overcount);

    public int Completed => _latencies.Count;

    public long LastCompletion => Interlocked.Read(ref _last);

    public IReadOnlyCollection<double> LatenciesInMilliseconds => (IReadOnlyCollection<double>)_latencies.Values;

    public async ValueTask WorkAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _handlerRuns);
        if (HandlerDelay > TimeSpan.Zero)
            await Task.Delay(HandlerDelay, cancellationToken);
    }

    public void Start(string id) => _started[id] = Stopwatch.GetTimestamp();

    public void Complete(string id, int overcount)
    {
        var now = Stopwatch.GetTimestamp();
        if (!_started.TryGetValue(id, out var started) || !_latencies.TryAdd(id, Stopwatch.GetElapsedTime(started, now).TotalMilliseconds))
            return;

        if (overcount != 0)
            Interlocked.Add(ref _overcount, Math.Abs(overcount));

        long current;
        while ((current = Interlocked.Read(ref _last)) < now &&
               Interlocked.CompareExchange(ref _last, now, current) != current)
        {
        }
    }

    public void Reset()
    {
        _started.Clear();
        _latencies.Clear();
        Interlocked.Exchange(ref _handlerRuns, 0);
        Interlocked.Exchange(ref _overcount, 0);
        Interlocked.Exchange(ref _last, 0);
    }
}

/// <summary>
/// Counts what the transport hands to the pipeline, and what the pipeline gives back to it as a lock conflict.
/// </summary>
public sealed class DeliveryCounter
{
    private long _deliveries;
    private long _lockConflicts;
    private long _otherFailures;

    public long Deliveries => Interlocked.Read(ref _deliveries);
    public long LockConflicts => Interlocked.Read(ref _lockConflicts);
    public long OtherFailures => Interlocked.Read(ref _otherFailures);

    public void Delivered() => Interlocked.Increment(ref _deliveries);
    public void LockConflict() => Interlocked.Increment(ref _lockConflicts);
    public void OtherFailure() => Interlocked.Increment(ref _otherFailures);

    public void Reset()
    {
        Interlocked.Exchange(ref _deliveries, 0);
        Interlocked.Exchange(ref _lockConflicts, 0);
        Interlocked.Exchange(ref _otherFailures, 0);
    }
}

public sealed class CountingMessageProcessor(IMessageProcessor inner, DeliveryCounter counter) : IMessageProcessor
{
    public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
    {
        counter.Delivered();
        try
        {
            await inner.ProcessAsync(outboxMessage, cancellationToken);
        }
        catch (Exception ex) when (ex.IsLockConflict())
        {
            counter.LockConflict();
            throw;
        }
        catch
        {
            counter.OtherFailure();
            throw;
        }
    }
}
