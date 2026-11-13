using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger<KafkaMessageSubscriber> _logger;

    private readonly string[] _topicNames;
    private readonly RetryTopics _retryTopics;
    private readonly TimeSpan _retryDelay;

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

    /// <param name="systemInfo">tells which messages of a retry topic are for this client group. When missing, all of them are processed.</param>
    /// <param name="kafkaConfiguration">its retry delay is the longest a message is kept waiting. Defaults to 30 seconds.</param>
    public KafkaMessageSubscriber(
        IConsumerBuilderFactory builderFactory,
        ISagaDescriptorsResolver sagaDescriptorsResolver,
        IQueueReferenceFactory queueReferenceFactory,
        IKafkaMessageHandler messageHandler,
        KafkaSubscriberConfig? config = null,
        ISystemInfo? systemInfo = null,
        KafkaConfiguration? kafkaConfiguration = null,
        ILogger<KafkaMessageSubscriber>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builderFactory);
        ArgumentNullException.ThrowIfNull(sagaDescriptorsResolver);

        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _config = config ?? KafkaSubscriberConfig.Default;
        _logger = logger ?? NullLogger<KafkaMessageSubscriber>.Instance;
        _retryDelay = kafkaConfiguration?.RetryDelay ?? KafkaConfiguration.DefaultRetryDelay;

        var queueReferences = sagaDescriptorsResolver.GetRegisteredMessageTypes()
                                                     .Select(messageType => queueReferenceFactory.Create(messageType))
                                                     .ToArray();
        var retryTopicNames = queueReferences.Select(references => references.RetryTopicName)
                                             .Where(topicName => !string.IsNullOrWhiteSpace(topicName))
                                             .ToArray();
        _topicNames = queueReferences.Select(references => references.TopicName)
                                     .Concat(retryTopicNames)
                                     .Distinct()
                                     .ToArray();
        _retryTopics = new RetryTopics(retryTopicNames, systemInfo?.ClientGroup, _retryDelay);

        var builder = builderFactory.Create<string, byte[]>();
        ForgetHeldMessagesOnRebalance(builder);
        _consumer = builder.Build();
    }

    // a partition that is taken away from this consumer goes to another one, which receives what was not stored
    // as handled: the offsets of the messages handled so far are stored first, and the messages held for it here
    // are forgotten, or they would be processed twice, and behind them the ones that arrive when the partition comes back
    private void ForgetHeldMessagesOnRebalance(ConsumerBuilder<string, byte[]> builder)
    {
        try
        {
            builder.SetPartitionsRevokedHandler((_, partitions) =>
            {
                StoreHandledOffsets();
                ForgetHeldMessages(partitions);
            });
            builder.SetPartitionsLostHandler((_, partitions) =>
            {
                // already someone else's: there is nothing left to store offsets on
                ForgetHandledOffsets(partitions);
                ForgetHeldMessages(partitions);
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "the Kafka consumer builder already has a rebalance handler: messages handled just before their partition is taken away, or waiting to be processed again, can be processed twice");
        }
    }

    private void ForgetHandledOffsets(List<TopicPartitionOffset> partitions)
    {
        foreach (var partition in partitions)
        {
            _lastHandledOfOtherPartitions.Remove(partition.TopicPartition);

            if (_lastHandled is not null && _lastHandled.TopicPartition == partition.TopicPartition)
                _lastHandled = null;
        }
    }

    // called by the consumer from inside Consume or Close, on the thread of the consume loop
    private void ForgetHeldMessages(List<TopicPartitionOffset> partitions)
    {
        if (_heldMessages.IsEmpty)
            return;

        foreach (var partition in partitions)
        {
            if (!_heldMessages.Forget(partition.TopicPartition))
                continue;

            _logger.LogInformation(
                "partition '{Partition}' was taken away: the messages waiting to be processed again are left to its next consumer",
                partition.TopicPartition);

            // a pause that outlived the assignment would leave the partition paused when it comes back
            ResumePartition(partition.TopicPartition);
        }
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
        try
        {
            _consumer?.Subscribe(_topicNames);

            while (!stoppingToken.IsCancellationRequested)
            {
                await ConsumeMessageAsync(stoppingToken);

                if (_config.ConsumeDelay > TimeSpan.Zero)
                    await Task.Delay(_config.ConsumeDelay, stoppingToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // nobody observes the loop: without this it would end without a trace
            _logger.LogCritical(ex, "the Kafka consume loop has stopped, no more messages are consumed: {Error}", ex.Message);
            throw;
        }
        finally
        {
            // closing the consumer commits what is stored
            StoreHandledOffsets();
        }
    }

    /// <summary>
    /// consumes a single message, after processing a held one whose time has come
    /// </summary>
    private async ValueTask ConsumeMessageAsync(CancellationToken stoppingToken)
    {
        try
        {
            var consumeTimeout = _config.ConsumeTimeout;
            if (!_heldMessages.IsEmpty)
            {
                // one at a time: the consumer has to be polled between messages, or the group takes it for dead
                await ProcessDueMessageAsync(stoppingToken);
                consumeTimeout = Min(consumeTimeout, _heldMessages.TimeToNextDue(DateTimeOffset.UtcNow));
            }

            var result = _consumer.Consume((int)Math.Ceiling(consumeTimeout.TotalMilliseconds));
            var canProcess = (result is not null && !result.IsPartitionEOF);
            if (!canProcess)
            {
                // nothing to do right now: a good moment for what is cheaper done once in a while
                StoreHandledOffsets();
            }
            else if (!TryHoldOrSkip(result!))
            {
                await HandleAsync(result!, stoppingToken);
            }
        }
        catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
        {
            // seems to be a known issue in the c# Kafka driver, occurring when consumers are started before producers.
            // The error comes back without waiting for the consume timeout, so the loop waits here instead
            await Task.Delay(_config.ConsumeTimeout, stoppingToken);
        }
        catch (ConsumeException ex) when (ex.Error is null || !ex.Error.IsFatal)
        {
            // one topic that cannot be read, a retry topic the client is not authorized on for instance,
            // does not keep the others from being consumed
            _logger.LogError(ex, "an error has occurred while consuming from Kafka topic '{Topic}', the other topics are still consumed: {Error}",
                ex.ConsumerRecord?.Topic, ex.Message);
            await Task.Delay(_config.ConsumeTimeout, stoppingToken);
        }
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;

    /// <returns>true when there is nothing to do with the message right now: it has to wait, or it is not for this consumer.</returns>
    private bool TryHoldOrSkip(ConsumeResult<string, byte[]> result)
    {
        // a broker may still deliver what it had sent before the partition was paused: it waits its turn behind the held message
        if (!_heldMessages.IsEmpty && _heldMessages.IsHolding(result.TopicPartition))
        {
            _heldMessages.Hold(result, DueAt(result, DateTimeOffset.UtcNow));
            return true;
        }

        // a not-before time only means something where this library wrote it
        if (!_retryTopics.Contains(result.Topic))
            return false;

        if (_retryTopics.IsForAnotherGroup(result.Message?.Headers))
        {
            MarkAsHandled(result);
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var dueAt = _retryTopics.DueAt(result.Message?.Headers, now);
        if (dueAt <= now)
            return false;

        HoldAndPausePartition(result, dueAt);
        return true;
    }

    private DateTimeOffset DueAt(ConsumeResult<string, byte[]> result, DateTimeOffset now)
        => _retryTopics.Contains(result.Topic) ? _retryTopics.DueAt(result.Message?.Headers, now) : now;

    // a retry topic only receives messages that have to wait for the same delay, so the first one is the first to be due:
    // nothing else is fetched from its partition until then, while the other partitions keep flowing
    private void HoldAndPausePartition(ConsumeResult<string, byte[]> result, DateTimeOffset dueAt)
    {
        _heldMessages.Hold(result, dueAt);

        _logger.LogDebug("the message at '{Offset}' is held until {DueAt}", result.TopicPartitionOffset, dueAt);

        try
        {
            _consumer.Pause([result.TopicPartition]);
        }
        catch (KafkaException ex)
        {
            // whatever still arrives from the partition is held as well
            _logger.LogWarning(ex, "unable to pause partition '{Partition}': {Error}", result.TopicPartition, ex.Message);
        }
    }

    private async ValueTask ProcessDueMessageAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested
            || !_heldMessages.TryTakeDue(DateTimeOffset.UtcNow, out var result, out var isLastOfPartition))
            return;

        var isHandled = _retryTopics.Contains(result.Topic) && _retryTopics.IsForAnotherGroup(result.Message?.Headers)
                        || await _messageHandler.HandleAsync(result, stoppingToken);
        if (!isHandled)
        {
            if (!stoppingToken.IsCancellationRequested)
                _heldMessages.HoldFirst(result, RedeliveryTime(result));
            return;
        }

        MarkAsHandled(result);

        if (isLastOfPartition)
            ResumePartition(result.TopicPartition);
    }

    private void ResumePartition(TopicPartition partition)
    {
        try
        {
            _consumer.Resume([partition]);
        }
        catch (KafkaException ex)
        {
            // the partition was taken away in the meantime, and whoever has it now is not paused
            _logger.LogWarning(ex, "unable to resume partition '{Partition}': {Error}", partition, ex.Message);
        }
    }

    private async ValueTask HandleAsync(ConsumeResult<string, byte[]> result, CancellationToken stoppingToken)
    {
        if (await _messageHandler.HandleAsync(result, stoppingToken))
        {
            MarkAsHandled(result);
            return;
        }

        // a message whose processing was interrupted by a stop is left for the next consumer of the partition
        if (stoppingToken.IsCancellationRequested)
            return;

        // storing the offset of the next message would also mark this one as consumed:
        // nothing else is read from its partition until it is dealt with
        HoldAndPausePartition(result, RedeliveryTime(result));
    }

    private DateTimeOffset RedeliveryTime(ConsumeResult<string, byte[]> result)
    {
        _logger.LogWarning(
            "the message at '{Offset}' could neither be processed nor set aside: it is processed again in {Delay}, and its partition waits for it",
            result.TopicPartitionOffset, _retryDelay);

        return DateTimeOffset.UtcNow + _retryDelay;
    }

    // the offset of a message is stored, and later committed, only once the message was dealt with:
    // one that was not, because the process died or stopped or because it is still held, is delivered again.
    // Storing an offset covers every message before it in the partition, and is a call into the native client
    // that costs a tenth of the consumer's throughput when made for every message: it is made for the last message
    // handled of each partition, every so many messages, whenever the consumer is idle, and before a partition
    // is given up. A message that was handled can be delivered again if the process dies before that, never skipped
    private void MarkAsHandled(ConsumeResult<string, byte[]> result)
    {
        if (!_storesOffsets)
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
        _handledSinceOffsetsStored = 0;

        // the most recent message goes last: its partition may also be among the others, with an older message
        if (_lastHandledOfOtherPartitions.Count > 0)
        {
            foreach (var result in _lastHandledOfOtherPartitions.Values)
                StoreOffset(result);
            _lastHandledOfOtherPartitions.Clear();
        }

        if (_lastHandled is null)
            return;

        StoreOffset(_lastHandled);
        _lastHandled = null;
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
            _storesOffsets = false;
            _logger.LogWarning(ex,
                "the Kafka consumer stores offsets by itself (enable.auto.offset.store): a message that is being processed, or is waiting to be processed again, " +
                "when the consumer stops is not delivered again. Set enable.auto.offset.store to false in the IConsumerBuilderFactory");
        }
        catch (KafkaException ex)
        {
            // the partition was taken away in the meantime: its next consumer starts from the last committed offset
            _logger.LogWarning(ex, "unable to store the offset '{Offset}', its message may be delivered again: {Error}",
                result.TopicPartitionOffset, ex.Message);
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
