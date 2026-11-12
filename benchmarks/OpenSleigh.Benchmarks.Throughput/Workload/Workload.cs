using OpenSleigh.Transport;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenSleigh.Benchmarks.Throughput.Workload;

public sealed record StartBench : IMessage, IHasCorrelationId
{
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed record BenchStep : IMessage
{
    public int Step { get; init; }
}

public sealed record Ping : IMessage, IHasCorrelationId
{
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed class BenchState
{
    public int Steps { get; set; }
}

/// <summary>
/// Two-hop saga: the start message publishes a step, the step completes the saga.
/// Every hop goes through the outbox, the relay, the broker and the consumer.
/// </summary>
public sealed class BenchSaga : Saga<BenchState>, IStartedBy<StartBench>, IHandleMessage<BenchStep>
{
    private readonly WorkTracker _tracker;

    public BenchSaga(ISagaInstance<BenchState> context, WorkTracker tracker) : base(context)
    {
        _tracker = tracker;
    }

    public ValueTask HandleAsync(IMessageContext<StartBench> messageContext, CancellationToken cancellationToken = default)
    {
        Context.State.Steps++;
        Publish(new BenchStep { Step = Context.State.Steps });
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<BenchStep> messageContext, CancellationToken cancellationToken = default)
    {
        Context.State.Steps++;
        Context.MarkAsCompleted();
        _tracker.Complete(messageContext.CorrelationId);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Single-message saga used to measure a consumer on its own.
/// </summary>
public sealed class PingSaga : Saga, IStartedBy<Ping>
{
    private readonly WorkTracker _tracker;

    public PingSaga(ISagaInstance context, WorkTracker tracker) : base(context)
    {
        _tracker = tracker;
    }

    public async ValueTask HandleAsync(IMessageContext<Ping> messageContext, CancellationToken cancellationToken = default)
    {
        if (_tracker.HandlerDelay > TimeSpan.Zero)
            await Task.Delay(_tracker.HandlerDelay, cancellationToken);

        Context.MarkAsCompleted();
        _tracker.Complete(messageContext.CorrelationId);
    }
}

/// <summary>
/// Records which units of work completed, and when the first and the last one did.
/// </summary>
public sealed class WorkTracker
{
    private readonly ConcurrentDictionary<string, byte> _completed = new();
    private long _first;
    private long _last;

    public TimeSpan HandlerDelay { get; set; } = TimeSpan.Zero;

    public int Count => _completed.Count;

    /// <summary>timestamp of the last completion, zero when there is none.</summary>
    public long LastCompletion => Interlocked.Read(ref _last);

    public void Complete(string id)
    {
        if (!_completed.TryAdd(id, 0))
            return;

        var now = Stopwatch.GetTimestamp();
        Interlocked.CompareExchange(ref _first, now, 0);

        long current;
        while ((current = Interlocked.Read(ref _last)) < now &&
               Interlocked.CompareExchange(ref _last, now, current) != current)
        {
        }
    }

    public void Reset()
    {
        _completed.Clear();
        Interlocked.Exchange(ref _first, 0);
        Interlocked.Exchange(ref _last, 0);
    }

    /// <summary>completions per second between the first and the last completion.</summary>
    public double RateBetweenFirstAndLast()
    {
        var count = Count;
        var first = Interlocked.Read(ref _first);
        var last = Interlocked.Read(ref _last);
        if (count < 2 || last <= first)
            return 0;
        return (count - 1) / Stopwatch.GetElapsedTime(first, last).TotalSeconds;
    }

    /// <summary>completions per second between <paramref name="startTimestamp"/> and the last completion.</summary>
    public double RateSince(long startTimestamp)
    {
        var count = Count;
        var last = Interlocked.Read(ref _last);
        if (count == 0 || last <= startTimestamp)
            return 0;
        return count / Stopwatch.GetElapsedTime(startTimestamp, last).TotalSeconds;
    }
}

public sealed record BenchMessageContext<TM>(TM Message, string MessageId, string CorrelationId, string SenderId)
    : IMessageContext<TM> where TM : IMessage;
