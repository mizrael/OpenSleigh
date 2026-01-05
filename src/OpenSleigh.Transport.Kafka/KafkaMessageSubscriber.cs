using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka;

public record KafkaSubscriberConfig(TimeSpan ConsumeDelay, TimeSpan ConsumeTimeout)
{
    public static readonly KafkaSubscriberConfig Default = new(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
}

public sealed class KafkaMessageSubscriber : IMessageSubscriber, IDisposable
{
    private readonly IKafkaMessageHandler _messageHandler;
    private readonly KafkaSubscriberConfig _config;

    private readonly string[] _topicNames;

    private readonly IConsumer<string, byte[]> _consumer;
    private CancellationTokenSource? _stoppingCts;
    private Task? _consumerTask;

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
        _consumerTask = Task.Run(async () => await ConsumeMessages(_stoppingCts.Token), _stoppingCts.Token);
        return ValueTask.CompletedTask;
    }

    private async ValueTask ConsumeMessages(CancellationToken stoppingToken)
    {
        _consumer?.Subscribe(_topicNames);

        while (!stoppingToken.IsCancellationRequested)
        {
            var canContinue = await ConsumeMessageAsync(stoppingToken);
        //    if (!canContinue)
        //        break;

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
            if (!canProcess)
                return false;

            return await _messageHandler.HandleAsync(result!, stoppingToken);
        }
        catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
        {
            // noop. seems to be a known issue in the c# Kafka driver
            // occurring when consumers are started before producers.
            return true;
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
