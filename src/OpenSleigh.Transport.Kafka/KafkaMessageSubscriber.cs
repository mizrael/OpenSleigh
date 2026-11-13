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
    private CancellationTokenSource? _stoppingCts;
    private Task? _consumerTask;
    private int _consumerClosed;

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
        _topicNames = messageTypes.Select(mt => queueReferenceFactory.Create(mt).TopicName).ToArray();
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _config = config ?? KafkaSubscriberConfig.Default;
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

        while (!stoppingToken.IsCancellationRequested)
        {
            await ConsumeMessageAsync(stoppingToken);

            if (_config.ConsumeDelay > TimeSpan.Zero)
                await Task.Delay(_config.ConsumeDelay, stoppingToken);
        }
    }

    /// <summary>
    /// consumes a single message 
    /// </summary>
    /// <returns>false if consumer loop should be stopped</returns>
    private async ValueTask ConsumeMessageAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = _consumer.Consume((int)_config.ConsumeTimeout.TotalMilliseconds);
            var canProcess = (result is not null && !result.IsPartitionEOF);
            if (canProcess)
                await _messageHandler.HandleAsync(result!, stoppingToken);
        }
        catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
        {
            // seems to be a known issue in the c# Kafka driver, occurring when consumers are started before producers.
            // The error comes back without waiting for the consume timeout, so the loop waits here instead
            await Task.Delay(_config.ConsumeTimeout, stoppingToken);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _stoppingCts?.Cancel();

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

        CloseConsumer();
    }

    // closing a consumer also disposes it, and a disposed consumer cannot be closed again:
    // a host stops its subscribers and then disposes them
    private void CloseConsumer()
    {
        if (Interlocked.Exchange(ref _consumerClosed, 1) == 0)
            _consumer.Close();
    }

    public void Dispose()
    {
        _stoppingCts?.Cancel();
        _stoppingCts?.Dispose();
        CloseConsumer();
        _consumer.Dispose();
    }
}
