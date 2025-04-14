using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using System.Text;

namespace OpenSleigh.Transport.Kafka;

public class KafkaPublisher : IPublisher, IKafkaPublisherExecutor
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly ILogger<KafkaPublisher> _logger;
    private readonly IQueueReferenceFactory _queueReferenceFactory;

    public KafkaPublisher(
        IQueueReferenceFactory queueReferenceFactory, 
        IProducer<string, byte[]> producer, 
        ILogger<KafkaPublisher> logger)
    {
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _producer = producer;
        _logger = logger;
    }

    public async ValueTask PublishAsync(
        OutboxMessage message, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message, nameof(message));

        var queueRefs = _queueReferenceFactory.Create(message);

        _logger.LogInformation("pushing message '{MessageId}' to topic '{Topic}' ...",
                               message.MessageId, queueRefs.TopicName);

        var result = await PublishAsyncCore(message, queueRefs.TopicName, null, cancellationToken: cancellationToken);
        if (result is null || result.Status == PersistenceStatus.NotPersisted)
            throw new InvalidOperationException($"unable to publish message '{message.MessageId}'");
    }

    public ValueTask<DeliveryResult<string, byte[]>> PublishAsync(
        OutboxMessage message,
        string topic,
        IEnumerable<Header>? additionalHeaders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message, nameof(message));
        ArgumentException.ThrowIfNullOrWhiteSpace(topic, nameof(topic));

        _logger.LogInformation("pushing message '{MessageId}' to topic '{Topic}' ...",
                                message.MessageId, topic);

        return PublishAsyncCore(message, topic, additionalHeaders, cancellationToken);
    }

    private async ValueTask<DeliveryResult<string, byte[]>> PublishAsyncCore(
        OutboxMessage message,
        string topic,
        IEnumerable<Header>? additionalHeaders,
        CancellationToken cancellationToken)
    {
        var headers = new Headers
        {
            { nameof(message.MessageType),  Encoding.UTF8.GetBytes(message.MessageType.FullName) },
            { nameof(message.ParentId),  Encoding.UTF8.GetBytes(message.ParentId ?? string.Empty) },
            { nameof(message.SenderId),  Encoding.UTF8.GetBytes(message.SenderId) },
            { nameof(message.CorrelationId),  Encoding.UTF8.GetBytes(message.CorrelationId) },
            { nameof(message.CreatedAt),  Encoding.UTF8.GetBytes(message.CreatedAt.ToString()) }
        };

        if (additionalHeaders is not null)
            foreach (var header in additionalHeaders)
                headers.Add(header);

        var kafkaMessage = new Message<string, byte[]>()
        {
            Key = message.MessageId,
            Value = message.Body.ToArray(),
            Headers = headers
        };

        var result = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
        return result;
    }
}
