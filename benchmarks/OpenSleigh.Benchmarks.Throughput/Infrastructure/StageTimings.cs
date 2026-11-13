using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence;
using OpenSleigh.Transport;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

/// <summary>
/// Where the time of a scenario goes: wall-clock time spent inside each call the pipeline makes
/// to its persistence and transport, measured by wrapping the registered services.
/// </summary>
internal sealed class StageTimings
{
    private readonly ConcurrentDictionary<string, Stage> _stages = new();

    public async ValueTask<T> MeasureAsync<T>(string stage, Func<ValueTask<T>> call)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            return await call().ConfigureAwait(false);
        }
        finally
        {
            Record(stage, start);
        }
    }

    public async ValueTask MeasureAsync(string stage, Func<ValueTask> call)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await call().ConfigureAwait(false);
        }
        finally
        {
            Record(stage, start);
        }
    }

    public void Reset() => _stages.Clear();

    public IReadOnlyList<(string Stage, int Calls, double TotalMilliseconds)> Snapshot()
        => _stages
            .Select(pair => (pair.Key, pair.Value.Calls, Stopwatch.GetElapsedTime(0, pair.Value.Ticks).TotalMilliseconds))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();

    private void Record(string stage, long start)
    {
        var elapsed = Stopwatch.GetTimestamp() - start;
        var entry = _stages.GetOrAdd(stage, _ => new Stage());
        Interlocked.Add(ref entry.Ticks, elapsed);
        Interlocked.Increment(ref entry.Calls);
    }

    private sealed class Stage
    {
        public long Ticks;
        public int Calls;
    }

    /// <summary>
    /// Wraps the saga state repository, the outbox repository, the publisher and the transaction manager.
    /// To be called after the persistence and the transport were registered.
    /// </summary>
    public static void Instrument(IServiceCollection services)
    {
        services.AddSingleton<StageTimings>();

        services.TryDecorate<ISagaStateRepository>((provider, inner) =>
        {
            var timings = provider.GetRequiredService<StageTimings>();
            return inner is ISagaStateCommitter
                ? new TimedCommittingSagaStateRepository(inner, timings)
                : new TimedSagaStateRepository(inner, timings);
        });
        services.TryDecorate<IOutboxRepository>((provider, inner) =>
        {
            var timings = provider.GetRequiredService<StageTimings>();
#if OUTBOX_BATCH_DELETE
            if (inner is IOutboxBatchRepository)
                return new TimedBatchOutboxRepository(inner, timings);
#endif
            return new TimedOutboxRepository(inner, timings);
        });
        services.TryDecorate<IPublisher>((provider, inner)
            => new TimedPublisher(inner, provider.GetRequiredService<StageTimings>()));
        services.TryDecorate<ITransactionManager>((provider, inner)
            => new TimedTransactionManager(inner, provider.GetRequiredService<StageTimings>()));
    }
}

internal static class Stages
{
    public const string Find = "consumer: saga FindAsync";
    public const string Lock = "consumer: saga LockAsync";
    public const string Commit = "consumer: saga CommitAsync";
    public const string Release = "consumer: saga ReleaseAsync";
    public const string Append = "outbox AppendAsync";
    public const string StartTransaction = "relay: StartTransactionAsync";
    public const string ReadPending = "relay: outbox ReadPendingAsync";
    public const string Publish = "relay: PublishAsync";
    public const string Delete = "relay: outbox DeleteAsync";
    public const string DeleteBatch = "relay: outbox DeleteAsync (batch)";
    public const string CommitTransaction = "relay: transaction CommitAsync";
}

internal class TimedSagaStateRepository : ISagaStateRepository
{
    protected readonly ISagaStateRepository Inner;
    protected readonly StageTimings Timings;

    public TimedSagaStateRepository(ISagaStateRepository inner, StageTimings timings)
    {
        Inner = inner;
        Timings = timings;
    }

    public ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
        => Timings.MeasureAsync(Stages.Find, () => Inner.FindAsync(descriptor, messageContext, cancellationToken));

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => Timings.MeasureAsync(Stages.Lock, () => Inner.LockAsync(state, cancellationToken));

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => Timings.MeasureAsync(Stages.Release, () => Inner.ReleaseAsync(state, cancellationToken));
}

internal sealed class TimedCommittingSagaStateRepository : TimedSagaStateRepository, ISagaStateCommitter
{
    public TimedCommittingSagaStateRepository(ISagaStateRepository inner, StageTimings timings) : base(inner, timings)
    {
    }

    public ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => Timings.MeasureAsync(Stages.Commit, () => ((ISagaStateCommitter)Inner).CommitAsync(state, cancellationToken));
}

internal class TimedOutboxRepository : IOutboxRepository
{
    protected readonly IOutboxRepository Inner;
    protected readonly StageTimings Timings;

    public TimedOutboxRepository(IOutboxRepository inner, StageTimings timings)
    {
        Inner = inner;
        Timings = timings;
    }

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
        => Timings.MeasureAsync(Stages.Append, () => Inner.AppendAsync(messages, cancellationToken));

    public ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
        => Timings.MeasureAsync(Stages.ReadPending, () => Inner.ReadPendingAsync(cancellationToken));

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        => Timings.MeasureAsync(Stages.Delete, () => Inner.DeleteAsync(message, cancellationToken));
}

#if OUTBOX_BATCH_DELETE
internal sealed class TimedBatchOutboxRepository : TimedOutboxRepository, IOutboxBatchRepository
{
    public TimedBatchOutboxRepository(IOutboxRepository inner, StageTimings timings) : base(inner, timings)
    {
    }

    public ValueTask DeleteAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default)
        => Timings.MeasureAsync(Stages.DeleteBatch, () => ((IOutboxBatchRepository)Inner).DeleteAsync(messages, cancellationToken));
}
#endif

internal sealed class TimedPublisher : IPublisher
{
    private readonly IPublisher _inner;
    private readonly StageTimings _timings;

    public TimedPublisher(IPublisher inner, StageTimings timings)
    {
        _inner = inner;
        _timings = timings;
    }

    public ValueTask PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        => _timings.MeasureAsync(Stages.Publish, () => _inner.PublishAsync(message, cancellationToken));
}

internal sealed class TimedTransactionManager : ITransactionManager
{
    private readonly ITransactionManager _inner;
    private readonly StageTimings _timings;

    public TimedTransactionManager(ITransactionManager inner, StageTimings timings)
    {
        _inner = inner;
        _timings = timings;
    }

    public async ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _timings.MeasureAsync(Stages.StartTransaction, () => _inner.StartTransactionAsync(cancellationToken));
        return new TimedTransaction(transaction, _timings);
    }

    private sealed class TimedTransaction : ITransaction
    {
        private readonly ITransaction _inner;
        private readonly StageTimings _timings;

        public TimedTransaction(ITransaction inner, StageTimings timings)
        {
            _inner = inner;
            _timings = timings;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken = default)
            => _timings.MeasureAsync(Stages.CommitTransaction, () => _inner.CommitAsync(cancellationToken));

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            => _inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
