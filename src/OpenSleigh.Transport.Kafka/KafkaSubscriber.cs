using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Transport.Kafka;

public class KafkaSubscriber<TMessage> : IMessageSubscriber<TMessage>, IDisposable
    where TMessage : IMessage
{
    private readonly IConsumer<string, byte[]> _consumer;
    private readonly QueueReferences _queueRef;
    private readonly IKafkaMessageHandler _messageHandler;
    private CancellationTokenSource? _cts;

    public KafkaSubscriber(
        IConsumerBuilderFactory builderFactory, 
        IQueueReferenceFactory queueReferenceFactory, 
        IKafkaMessageHandler messageHandler, 
        ILogger<KafkaSubscriber<IMessage>> logger)
    {
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));

        _consumer = builderFactory.Create<IMessage, string, byte[]>().Build();

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

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if(_cts is not null)
            throw new InvalidOperationException("The subscriber has already been started.");
        _cts = new CancellationTokenSource();

        Task.Run(() => ProcessQueueAsync(), _cts.Token);
        return ValueTask.CompletedTask;
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

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        return ValueTask.CompletedTask;
    }
}