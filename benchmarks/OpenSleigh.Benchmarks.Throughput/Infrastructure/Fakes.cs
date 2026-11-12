using Confluent.Kafka;
using OpenSleigh.Benchmarks.Throughput.Workload;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

/// <summary>
/// Publisher that goes nowhere: it only records which messages were relayed and when.
/// </summary>
internal sealed class CountingPublisher : IPublisher
{
    private readonly WorkTracker _tracker;

    public CountingPublisher(WorkTracker tracker)
    {
        _tracker = tracker;
    }

    public ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        _tracker.Complete(envelope.MessageId);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Publishes for real, and records which messages were relayed and when.
/// </summary>
internal sealed class TrackingPublisher : IPublisher
{
    private readonly IPublisher _inner;
    private readonly WorkTracker _tracker;

    public TrackingPublisher(IPublisher inner, WorkTracker tracker)
    {
        _inner = inner;
        _tracker = tracker;
    }

    public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        await _inner.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
        _tracker.Complete(envelope.MessageId);
    }
}

/// <summary>
/// When the relay last committed a pass: a message is out of the outbox then, not when it is published.
/// </summary>
internal sealed class CommitClock
{
    private long _last;

    public long Last => Interlocked.Read(ref _last);

    public void Tick() => Interlocked.Exchange(ref _last, System.Diagnostics.Stopwatch.GetTimestamp());

    public void Reset() => Interlocked.Exchange(ref _last, 0);
}

internal sealed class CommitTrackingTransactionManager : OpenSleigh.Persistence.ITransactionManager
{
    private readonly OpenSleigh.Persistence.ITransactionManager _inner;
    private readonly CommitClock _clock;

    public CommitTrackingTransactionManager(OpenSleigh.Persistence.ITransactionManager inner, CommitClock clock)
    {
        _inner = inner;
        _clock = clock;
    }

    public async ValueTask<OpenSleigh.Persistence.ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        => new Transaction(await _inner.StartTransactionAsync(cancellationToken).ConfigureAwait(false), _clock);

    private sealed class Transaction : OpenSleigh.Persistence.ITransaction
    {
        private readonly OpenSleigh.Persistence.ITransaction _inner;
        private readonly CommitClock _clock;

        public Transaction(OpenSleigh.Persistence.ITransaction inner, CommitClock clock)
        {
            _inner = inner;
            _clock = clock;
        }

        public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            await _inner.CommitAsync(cancellationToken).ConfigureAwait(false);
            _clock.Tick();
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            => _inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}

/// <summary>
/// In-process outbox that hands out pending messages in fixed-size batches, in insertion order,
/// the way a SQL outbox with a row limit does.
/// </summary>
internal sealed class BatchedFakeOutbox : IOutboxRepository
{
    private readonly ConcurrentDictionary<string, MessageEnvelope> _pending = new();
    private readonly ConcurrentQueue<string> _order = new();
    private readonly int _batchSize;

    public BatchedFakeOutbox(int batchSize)
    {
        _batchSize = batchSize;
    }

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            if (!_pending.TryAdd(message.MessageId, message))
                return ValueTask.FromResult(OutboxAppendResult.Duplicate);
            _order.Enqueue(message.MessageId);
        }
        return ValueTask.FromResult(OutboxAppendResult.Success);
    }

    public ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var batch = new List<MessageEnvelope>(_batchSize);
        foreach (var id in _order)
        {
            if (batch.Count == _batchSize)
                break;
            if (_pending.TryGetValue(id, out var message))
                batch.Add(message);
        }
        return ValueTask.FromResult<IEnumerable<MessageEnvelope>>(batch);
    }

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        _pending.TryRemove(message.MessageId, out _);
        while (_order.TryPeek(out var head) && !_pending.ContainsKey(head))
            _order.TryDequeue(out _);
        return ValueTask.CompletedTask;
    }
}

internal sealed class UnusedKafkaPublisher : IKafkaPublisherExecutor
{
    public ValueTask<DeliveryResult<string, byte[]>> PublishAsync(
        MessageEnvelope message,
        string topic,
        IEnumerable<Header>? additionalHeaders = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("the fake Kafka pipeline has no dead letter topic");
}

/// <summary>
/// A source of pre-built Kafka messages. When it runs dry it behaves like an idle broker:
/// Consume blocks for the requested timeout and returns nothing.
/// </summary>
internal sealed class FakeKafkaFeed
{
    private const string Topic = "bench.ping";

    private readonly ConcurrentQueue<ConsumeResult<string, byte[]>> _messages = new();

    public void Enqueue(MessageEnvelope envelope, ISerializer serializer)
    {
        var headers = new Headers
        {
            { nameof(envelope.MessageType), Encoding.UTF8.GetBytes(envelope.MessageType.FullName!) },
            { nameof(envelope.SenderId), Encoding.UTF8.GetBytes(envelope.SenderId) },
            { nameof(envelope.CorrelationId), Encoding.UTF8.GetBytes(envelope.CorrelationId) },
            { nameof(envelope.CreatedAt), Encoding.UTF8.GetBytes(envelope.CreatedAt.ToString()) }
        };

        _messages.Enqueue(new ConsumeResult<string, byte[]>
        {
            Topic = Topic,
            Message = new Message<string, byte[]>
            {
                Key = envelope.MessageId,
                Value = serializer.Serialize(envelope.Message),
                Headers = headers
            }
        });
    }

    public ConsumeResult<string, byte[]>? Next(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_messages.TryDequeue(out var message))
            return message;

        cancellationToken.WaitHandle.WaitOne(timeout);
        return null;
    }
}

/// <summary>
/// Stands in for IConsumer without a broker (and without librdkafka, which has no win-arm64 build).
/// </summary>
public class FakeKafkaConsumerProxy : DispatchProxy
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(250);

    internal FakeKafkaFeed? Feed { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == nameof(IConsumer<string, byte[]>.Consume))
        {
            var argument = args is { Length: > 0 } ? args[0] : null;
            return argument switch
            {
                int milliseconds => Feed!.Next(TimeSpan.FromMilliseconds(milliseconds), CancellationToken.None),
                TimeSpan timeout => Feed!.Next(timeout, CancellationToken.None),
                CancellationToken token => Feed!.Next(IdleTimeout, token),
                _ => Feed!.Next(IdleTimeout, CancellationToken.None)
            };
        }

        var returnType = targetMethod.ReturnType;
        return returnType.IsValueType && returnType != typeof(void)
            ? Activator.CreateInstance(returnType)
            : null;
    }
}

internal sealed class FakeConsumerBuilderFactory : IConsumerBuilderFactory
{
    private readonly FakeKafkaFeed _feed;

    public FakeConsumerBuilderFactory(FakeKafkaFeed feed)
    {
        _feed = feed;
    }

    public ConsumerBuilder<TKey, TValue> Create<TKey, TValue>()
        => new FakeConsumerBuilder<TKey, TValue>(_feed);

    private sealed class FakeConsumerBuilder<TKey, TValue> : ConsumerBuilder<TKey, TValue>
    {
        private readonly FakeKafkaFeed _feed;

        public FakeConsumerBuilder(FakeKafkaFeed feed)
            : base(new ConsumerConfig { GroupId = "bench", BootstrapServers = "localhost:9092" })
        {
            _feed = feed;
        }

        public override IConsumer<TKey, TValue> Build()
        {
            var consumer = DispatchProxy.Create<IConsumer<TKey, TValue>, FakeKafkaConsumerProxy>();
            ((FakeKafkaConsumerProxy)(object)consumer).Feed = _feed;
            return consumer;
        }
    }
}
