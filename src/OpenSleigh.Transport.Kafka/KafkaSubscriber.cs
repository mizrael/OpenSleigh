using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Transport.Kafka;

public class KafkaSubscriber<TMessage> : IMessageSubscriber<TMessage>, IDisposable
    where TMessage : IMessage
{
    private readonly IConsumer<string, ReadOnlyMemory<byte>> _consumer;
    private readonly QueueReferences _queueRef;
    private readonly IKafkaMessageHandler _messageHandler;
    private readonly ILogger<KafkaSubscriber<IMessage>> _logger;
    private CancellationTokenSource? _cts;

    public KafkaSubscriber(
        IConsumerBuilderFactory builderFactory, 
        IQueueReferenceFactory queueReferenceFactory, 
        IKafkaMessageHandler messageHandler, 
        ILogger<KafkaSubscriber<IMessage>> logger)
    {
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _consumer = builderFactory.Create<IMessage, string, ReadOnlyMemory<byte>>().Build();

        _queueRef = queueReferenceFactory.Create<IMessage>();
        _consumer.Subscribe(_queueRef.TopicName);
    }

    public void Dispose()
    {
        _consumer?.Close();
        _consumer?.Dispose();

        _cts?.Cancel();
        _cts?.Dispose();
    }

    public void Start()
    {
        if(_cts is not null)
            throw new InvalidOperationException("The subscriber has already been started.");
        _cts = new CancellationTokenSource();
        Task.Factory.StartNew(ProcessQueueAsync, TaskCreationOptions.LongRunning);
    }

    private async ValueTask ProcessQueueAsync()
    {
        if (_cts == null)
            throw new InvalidOperationException("The subscriber has not been started.");

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(_cts.Token);
                if (consumeResult is not null)
                    await _messageHandler.HandleAsync(consumeResult, _queueRef, _cts.Token)
                                         .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _consumer.Close();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}