using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Transport.Kafka;

public class KafkaSubscriber<TMessage> : IMessageSubscriber<TMessage>, IAsyncDisposable
    where TMessage : IMessage
{
    private readonly IConsumer<string, byte[]> _consumer;
    private readonly QueueReferences _queueRef;
    private readonly IKafkaMessageHandler _messageHandler;
    private readonly ILogger<KafkaSubscriber<IMessage>> _logger;
    private CancellationTokenSource? _stoppingCts;
    private Task? _consumerTask;

    public KafkaSubscriber(
        IConsumerBuilderFactory builderFactory, 
        IQueueReferenceFactory queueReferenceFactory, 
        IKafkaMessageHandler messageHandler, 
        ILogger<KafkaSubscriber<IMessage>> logger)
    {
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _logger = logger;
        _consumer = builderFactory.Create<IMessage, string, byte[]>().Build();

        _queueRef = queueReferenceFactory.Create<IMessage>();
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if(_stoppingCts is not null)
            throw new InvalidOperationException("The subscriber has already been started.");

        _stoppingCts = new CancellationTokenSource();

        _consumerTask = Task.Run(() => ProcessQueueAsync(), _stoppingCts.Token);

        return ValueTask.CompletedTask;
    }

    private async ValueTask ProcessQueueAsync()
    {
        if (_stoppingCts == null)
            throw new InvalidOperationException("The subscriber has not been started.");

        _logger.LogInformation("Starting Kafka subscriber for topic {TopicName} ...", _queueRef.TopicName);

        _consumer.Subscribe(_queueRef.TopicName);

        while (!_stoppingCts.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(_stoppingCts.Token);
                if (consumeResult is not null)
                    await _messageHandler.HandleAsync(consumeResult, _queueRef, _stoppingCts.Token)
                                         .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _consumer.Close();

        _logger.LogInformation("Kafka subscriber for topic {TopicName} stopped.", _queueRef.TopicName);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _stoppingCts?.Cancel();
            _stoppingCts?.Dispose();
            _stoppingCts = null;
        }
        finally
        {
            if (_consumerTask is not null)
                await Task.WhenAny(_consumerTask, Task.Delay(Timeout.Infinite, cancellationToken))
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                _logger.LogError(t.Exception, "Error while stopping Kafka subscriber.");
                        }, cancellationToken)
                        .ConfigureAwait(false);
            _consumerTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        _consumer.Dispose();
    }
}