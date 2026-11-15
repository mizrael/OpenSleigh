using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using Polly;
using RabbitMQ.Client;
using System.Globalization;

namespace OpenSleigh.Transport.RabbitMQ;

public class RabbitPublisher : IPublisher
{
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly RabbitConfiguration _rabbitConfig;
    private readonly ILogger<RabbitPublisher> _logger;
    private readonly IChannelFactory _channelFactory;
    private readonly ISerializer _serializer;
    private readonly HashSet<Type> _consumedMessageTypes;
    private readonly ITypeResolver _typeResolver;

    /// <param name="sagaDescriptorsResolver">tells which message types the sagas of this host handle.</param>
    /// <param name="systemInfo">tells whether this host only publishes.</param>
    /// <param name="typeResolver">
    /// is told the types this host publishes and does not consume: a message the broker returns is read back by its type.
    /// </param>
    public RabbitPublisher(
        IQueueReferenceFactory queueReferenceFactory,
        RabbitConfiguration rabbitConfig,
        IChannelFactory channelFactory,
        ILogger<RabbitPublisher> logger,
        ISerializer serializer,
        ISagaDescriptorsResolver sagaDescriptorsResolver,
        ISystemInfo systemInfo,
        ITypeResolver typeResolver)
    {
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _rabbitConfig = rabbitConfig ?? throw new ArgumentNullException(nameof(rabbitConfig));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        ArgumentNullException.ThrowIfNull(sagaDescriptorsResolver);
        ArgumentNullException.ThrowIfNull(systemInfo);

        _consumedMessageTypes = systemInfo.PublishOnly
            ? []
            : sagaDescriptorsResolver.GetRegisteredMessageTypes().ToHashSet();
    }

    public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var queueRef = _queueReferenceFactory.Create(envelope);

        var channel = await _channelFactory.GetPublishChannelAsync(cancellationToken);

        await EnsureCanPublishAsync(channel, envelope.MessageType, queueRef, cancellationToken);

        var properties = new BasicProperties();
        properties.Persistent = true;
        properties.MessageId = envelope.MessageId;
        properties.CorrelationId = envelope.CorrelationId;
        properties.Headers = new Dictionary<string, object?>()
        {
            { nameof(envelope.MessageType), envelope.MessageType.FullName },
            { nameof(envelope.SenderId), envelope.SenderId },
            { nameof(envelope.CreatedAt), envelope.CreatedAt.ToString() }
        };

        // so that this host knows, when the broker returns the message again, for how long it has been coming back
        if (envelope.FirstReturnedAt is { } firstReturnedAt)
            properties.Headers.Add(nameof(envelope.FirstReturnedAt), firstReturnedAt.ToString("O", CultureInfo.InvariantCulture));

        var body = _serializer.Serialize(envelope.Message);

        var policy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
            {
                _logger.LogWarning(ex,
                    "Could not publish message '{MessageId}' to Exchange '{ExchangeName}', after {Timeout}s : {ExceptionMessage}",
                    envelope.MessageId,
                    queueRef.ExchangeName,
                    $"{time.TotalSeconds:n1}", ex.Message);
            });

        await policy.ExecuteAsync(async () =>
        {
            // mandatory: a message that no queue takes is sent back by the broker, and goes back to the outbox
            await channel.BasicPublishAsync(
                exchange: queueRef.ExchangeName,
                routingKey: queueRef.RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body);

            _logger.LogInformation("message '{MessageId}' published to Exchange '{ExchangeName}'",
                envelope.MessageId,
                queueRef.ExchangeName);
        });
    }

    // the queue of a message this host consumes is declared here as well: the relay can publish before the subscriber
    // has declared it, and the message would be lost. Any other queue would keep a copy of every message, for nobody.
    // The types this host consumes are known to the type resolver already: the others are made known here,
    // whatever store the message comes from
    private ValueTask EnsureCanPublishAsync(IChannel channel, Type messageType, QueueReferences queueRef, CancellationToken cancellationToken)
    {
        if (_consumedMessageTypes.Contains(messageType))
            return channel.EnsureTopologyAsync(queueRef, _rabbitConfig, cancellationToken);

        // once per type: this runs for every message, and the resolver looks a type up by its names
        if (!_publishedMessageTypes.ContainsKey(messageType))
        {
            _typeResolver.Register(messageType);
            _publishedMessageTypes.TryAdd(messageType, 0);
        }

        return channel.EnsureExchangeAsync(queueRef, _rabbitConfig, cancellationToken);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, byte> _publishedMessageTypes = new();
}