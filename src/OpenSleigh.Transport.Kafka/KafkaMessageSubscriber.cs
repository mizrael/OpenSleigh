using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Transport.Kafka;

public record KafkaSubscriberConfig(TimeSpan ConsumeDelay, TimeSpan ConsumeTimeout)
{
    public static readonly KafkaSubscriberConfig Default = new(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
}

public sealed class KafkaMessageSubscriber<TM> : IMessageSubscriber<TM>, IDisposable
    where TM : IMessage
{
    private readonly QueueReferences _queueReferences;
    private readonly IKafkaMessageHandler _messageHandler;
    private readonly ILogger<KafkaMessageSubscriber<TM>> _logger;
    private readonly KafkaSubscriberConfig _config;

    private readonly IConsumer<string, byte[]> _consumer;
    private CancellationTokenSource? _stoppingCts;
    private Task? _consumerTask;

    public KafkaMessageSubscriber(
        IConsumerBuilderFactory builderFactory,
        IQueueReferenceFactory queueReferenceFactory,
        IKafkaMessageHandler messageHandler,
        ILogger<KafkaMessageSubscriber<TM>> logger,
        KafkaSubscriberConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(builderFactory);

        ArgumentNullException.ThrowIfNull(queueReferenceFactory);

        var builder = builderFactory.Create<TM, string, byte[]>();
        _consumer = builder.Build();

        _queueReferences = queueReferenceFactory.Create<TM>();
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? KafkaSubscriberConfig.Default;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _consumerTask = Task.Run(async () => await ConsumeMessages(_stoppingCts.Token), _stoppingCts.Token);
        return ValueTask.CompletedTask;
    }

    private async ValueTask ConsumeMessages(CancellationToken stoppingToken)
    {
        _consumer?.Subscribe(_queueReferences.TopicName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var canContinue = await ConsumeMessageAsync(stoppingToken);
            if (!canContinue)
                break;

            // TODO: check if it's possible to get rid of this
            await Task.Delay(_config.ConsumeDelay, stoppingToken);
        }
    }

    /// <summary>
    /// consumes a single message 
    /// </summary>
    /// <returns>false if consumer loop should be stopped</returns>
    private async ValueTask<bool> ConsumeMessageAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = _consumer.Consume((int)_config.ConsumeTimeout.TotalMilliseconds);
            var canProcess = (result is not null && !result.IsPartitionEOF);
            if (canProcess)
                await _messageHandler.HandleAsync(result!, _queueReferences, stoppingToken);

            return true;
        }
        catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
        {
            // noop. seems to be a known issue in the c# Kafka driver
            // occurring when consumers are started before producers.

            _logger.LogWarning(ex, "Topic '{Topic}' still not available : {Exception}",
                _queueReferences.TopicName, ex.Message);
            await Task.Delay(_config.ConsumeDelay, stoppingToken);
            return true;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "consumer closed on Topic '{Topic}', probably during Dispose() call",
                _queueReferences.TopicName);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'",
                _queueReferences.TopicName);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'",
                _queueReferences.TopicName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "an error has occurred while consuming messages from Topic '{Topic}': {Exception}",
                _queueReferences.TopicName, ex.Message);
        }

        return false;
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

        _consumer.Close();
    }

    public void Dispose()
    {
        _stoppingCts?.Cancel();
        _stoppingCts?.Dispose();
        _consumer.Close();
        _consumer.Dispose();
    }
}
