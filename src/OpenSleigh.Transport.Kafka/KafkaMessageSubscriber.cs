using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka;

/// <param name="ConsumeDelay">pause after every consume attempt. Defaults to none: an idle consumer already waits inside the consume call for up to <paramref name="ConsumeTimeout"/>.</param>
/// <param name="ConsumeTimeout">how long a consume attempt waits for a message before giving up.</param>
public record KafkaSubscriberConfig(TimeSpan ConsumeDelay, TimeSpan ConsumeTimeout)
{
    public static readonly KafkaSubscriberConfig Default = new(TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
}

public sealed class KafkaMessageSubscriber : IMessageSubscriber, IDisposable
{
    private readonly IKafkaMessageHandler _messageHandler;
    private readonly KafkaSubscriberConfig _config;

    private readonly string[] _topicNames;

    private readonly IConsumer<string, byte[]> _consumer;
    private readonly HeldMessages _heldMessages = new();
    private readonly Dictionary<TopicPartition, ConsumeResult<string, byte[]>> _lastHandledOfOtherPartitions = new();
    private ConsumeResult<string, byte[]>? _lastHandled;
    private int _handledSinceOffsetsStored;
    private bool _storesOffsets = true;

    private const int MaxHandledBeforeOffsetsAreStored = 100;
    private CancellationTokenSource? _stoppingCts;
    private Task? _consumerTask;
    private int _consumerClosed;
    private int _disposed;

    private static readonly TimeSpan ConsumeLoopStopTimeout = TimeSpan.FromSeconds(10);

    public KafkaMessageSubscriber(
        IConsumerBuilderFactory builderFactory,
        ISagaDescriptorsResolver sagaDescriptorsResolver,
        IQueueReferenceFactory queueReferenceFactory,
        IKafkaMessageHandler messageHandler,
        KafkaSubscriberConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(builderFactory);

        ArgumentNullException.ThrowIfNull(sagaDescriptorsResolver);

        var builder = builderFactory.Create<string, byte[]>();
        _consumer = builder.Build();

        var messageTypes = sagaDescriptorsResolver.GetRegisteredMessageTypes();
        _topicNames = messageTypes.Select(messageType => queueReferenceFactory.Create(messageType))
                                  .SelectMany(TopicsToConsume)
                                  .Distinct()
                                  .ToArray();
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _config = config ?? KafkaSubscriberConfig.Default;
    }

    private static IEnumerable<string> TopicsToConsume(QueueReferences queueReferences)
    {
        yield return queueReferences.TopicName;

        if (!string.IsNullOrWhiteSpace(queueReferences.RetryTopicName))
            yield return queueReferences.RetryTopicName;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = _stoppingCts.Token;

        // Consume blocks its caller for up to the consume timeout, over and over, for as long as the application runs:
        // the loop gets a thread of its own instead of keeping one of the thread pool's busy
        _consumerTask = Task.Factory.StartNew(
            () => ConsumeMessages(stoppingToken).AsTask().GetAwaiter().GetResult(),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return ValueTask.CompletedTask;
    }

    private async ValueTask ConsumeMessages(CancellationToken stoppingToken)
    {
        _consumer?.Subscribe(_topicNames);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ConsumeMessageAsync(stoppingToken);

                if (_config.ConsumeDelay > TimeSpan.Zero)
                    await Task.Delay(_config.ConsumeDelay, stoppingToken);
            }
        }
        finally
        {
            // closing the consumer commits what is stored
            StoreHandledOffsets();
        }
    }

    /// <summary>
    /// consumes a single message, after processing the held ones whose time has come
    /// </summary>
    private async ValueTask ConsumeMessageAsync(CancellationToken stoppingToken)
    {
        try
        {
            var consumeTimeout = _config.ConsumeTimeout;
            if (!_heldMessages.IsEmpty)
            {
                await ProcessDueMessagesAsync(stoppingToken);
                consumeTimeout = Min(consumeTimeout, _heldMessages.TimeToNextDue(DateTimeOffset.UtcNow));
            }

            var result = _consumer.Consume((int)consumeTimeout.TotalMilliseconds);
            var canProcess = (result is not null && !result.IsPartitionEOF);
            if (!canProcess)
            {
                // nothing to do right now: a good moment for what is cheaper done once in a while
                StoreHandledOffsets();
            }
            else if (!TryHold(result!))
            {
                await _messageHandler.HandleAsync(result!, stoppingToken);
                MarkAsHandled(result!, stoppingToken);
            }
        }
        catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
        {
            // seems to be a known issue in the c# Kafka driver, occurring when consumers are started before producers.
            // The error comes back without waiting for the consume timeout, so the loop waits here instead
            await Task.Delay(_config.ConsumeTimeout, stoppingToken);
        }
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;

    /// <returns>true when the message has to wait: it is kept, and processed when its time comes.</returns>
    private bool TryHold(ConsumeResult<string, byte[]> result)
    {
        // a broker may still deliver what it had sent before the partition was paused: it waits its turn behind the held message
        if (!_heldMessages.IsEmpty && _heldMessages.IsHolding(result.TopicPartition))
        {
            _heldMessages.Hold(result, NotBeforeOf(result));
            return true;
        }

        if (!RetrySchedule.TryGetNotBefore(result.Message?.Headers, out var notBefore) || notBefore <= DateTimeOffset.UtcNow)
            return false;

        HoldAndPausePartition(result, notBefore);
        return true;
    }

    private static DateTimeOffset NotBeforeOf(ConsumeResult<string, byte[]> result)
        => RetrySchedule.TryGetNotBefore(result.Message?.Headers, out var notBefore) ? notBefore : DateTimeOffset.MinValue;

    // a retry topic only receives messages that have to wait for the same delay, so the first one is the first to be due:
    // nothing else is fetched from its partition until then, while the other partitions keep flowing
    private void HoldAndPausePartition(ConsumeResult<string, byte[]> result, DateTimeOffset notBefore)
    {
        _heldMessages.Hold(result, notBefore);

        try
        {
            _consumer.Pause([result.TopicPartition]);
        }
        catch (KafkaException)
        {
            // the partition was taken away in the meantime: whatever still arrives from it is held as well
        }
    }

    private async ValueTask ProcessDueMessagesAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested
               && _heldMessages.TryTakeDue(DateTimeOffset.UtcNow, out var result, out var isLastOfPartition))
        {
            await _messageHandler.HandleAsync(result, stoppingToken);
            MarkAsHandled(result, stoppingToken);

            if (isLastOfPartition)
                ResumePartition(result.TopicPartition);
        }
    }

    private void ResumePartition(TopicPartition partition)
    {
        try
        {
            _consumer.Resume([partition]);
        }
        catch (KafkaException)
        {
            // the partition was taken away in the meantime, and whoever has it now is not paused
        }
    }

    // the offset of a message is stored, and later committed, only once the message was dealt with:
    // one that was not, because the process died or stopped or because it is still held, is delivered again.
    // Storing an offset covers every message before it in the partition, and is a call into the native client:
    // it is made for the last message handled of each partition, once in a while, instead of once per message.
    // Until then a message that was handled can be delivered again, never skipped
    private void MarkAsHandled(ConsumeResult<string, byte[]> result, CancellationToken stoppingToken)
    {
        if (!_storesOffsets || stoppingToken.IsCancellationRequested)
            return;

        // messages mostly come in runs of the same partition: only the last one of a run is set aside
        var previous = _lastHandled;
        if (previous is not null && !IsSamePartition(previous, result))
            _lastHandledOfOtherPartitions[previous.TopicPartition] = previous;
        _lastHandled = result;

        if (++_handledSinceOffsetsStored >= MaxHandledBeforeOffsetsAreStored)
            StoreHandledOffsets();
    }

    private static bool IsSamePartition(ConsumeResult<string, byte[]> first, ConsumeResult<string, byte[]> second)
        => first.Partition.Value == second.Partition.Value && string.Equals(first.Topic, second.Topic, StringComparison.Ordinal);

    private void StoreHandledOffsets()
    {
        if (_lastHandled is null)
            return;

        // the most recent message goes last: its partition may also be among the others, with an older message
        foreach (var result in _lastHandledOfOtherPartitions.Values)
            StoreOffset(result);
        StoreOffset(_lastHandled);

        _lastHandledOfOtherPartitions.Clear();
        _lastHandled = null;
        _handledSinceOffsetsStored = 0;
    }

    private void StoreOffset(ConsumeResult<string, byte[]> result)
    {
        if (!_storesOffsets)
            return;

        try
        {
            _consumer.StoreOffset(result);
        }
        catch (KafkaException ex) when (ex.Error?.Code == ErrorCode.Local_InvalidArg)
        {
            // the consumer was built by a custom factory that left enable.auto.offset.store on, and stores offsets by itself
            _storesOffsets = false;
        }
        catch (KafkaException)
        {
            // the partition was taken away in the meantime: its next consumer starts from the last committed offset
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        RequestStop(Volatile.Read(ref _stoppingCts));

        if (_consumerTask is not null)
        {
            try
            {
                await _consumerTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        // the wait above was given up on: the loop is still using the consumer, which is left for Dispose to close
        if (_consumerTask is null || _consumerTask.IsCompleted)
            CloseConsumer();
    }

    private static void RequestStop(CancellationTokenSource? stoppingCts)
    {
        try
        {
            stoppingCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // disposed in the meantime, by a Dispose that has already asked the loop to stop
        }
    }

    // closing a consumer also disposes it, and a disposed consumer cannot be closed again:
    // a host stops its subscribers and then disposes them
    private void CloseConsumer()
    {
        if (Interlocked.Exchange(ref _consumerClosed, 1) == 0)
            _consumer.Close();
    }

    // the loop ends within a consume timeout once it is asked to, unless a handler is at work and ignores
    // the request: Dispose does not wait for that one for ever
    private void WaitForConsumeLoop()
    {
        try
        {
            _consumerTask?.Wait(ConsumeLoopStopTimeout);
        }
        catch (AggregateException)
        {
            // the loop ended by being cancelled, or failed: either way it is no longer using the consumer
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var stoppingCts = Interlocked.Exchange(ref _stoppingCts, null);
        RequestStop(stoppingCts);
        WaitForConsumeLoop();
        stoppingCts?.Dispose();

        CloseConsumer();
        _consumer.Dispose();
    }
}
