using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Text;

namespace OpenSleigh.Transport.Kafka;

public class KafkaPublisher : IPublisher, IKafkaPublisherExecutor
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly ILogger<KafkaPublisher> _logger;
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly ISerializer _serializer;

    public KafkaPublisher(
        IQueueReferenceFactory queueReferenceFactory,
        IProducer<string, byte[]> producer,
        ILogger<KafkaPublisher> logger,
        ISerializer serializer)
    {
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async ValueTask PublishAsync(
        MessageEnvelope message, 
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
        MessageEnvelope message,
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
        MessageEnvelope envelope,
        string topic,
        IEnumerable<Header>? additionalHeaders,
        CancellationToken cancellationToken)
    {
        var headers = new Headers
        {
            { nameof(envelope.MessageType),  Encoding.UTF8.GetBytes(envelope.MessageType.FullName!) },
            { nameof(envelope.SenderId),  Encoding.UTF8.GetBytes(envelope.SenderId) },
            { nameof(envelope.CorrelationId),  Encoding.UTF8.GetBytes(envelope.CorrelationId) },
            { nameof(envelope.CreatedAt),  Encoding.UTF8.GetBytes(envelope.CreatedAt.ToString()) }
        };

        if (additionalHeaders is not null)
            foreach (var header in additionalHeaders)
                headers.Add(header);

        var messageBody = _serializer.Serialize(envelope.Message);

        var kafkaMessage = new Message<string, byte[]>()
        {
            Key = envelope.MessageId,
            Value = messageBody,
            Headers = headers
        };

        var result = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
        return result;
    }
}
