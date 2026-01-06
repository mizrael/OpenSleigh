using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

internal sealed class RabbitMessageSubscriber : IAsyncDisposable, IMessageSubscriber
{
    private readonly IChannelFactory _channelFactory;
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly RabbitConfiguration _rabbitConfiguration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITypeResolver _typeResolver;
    private readonly ISerializer _serializer;
    private readonly ILogger<RabbitMessageSubscriber> _logger;

    private IChannel? _channel;

    public RabbitMessageSubscriber(
        IChannelFactory channelFactory,
        IQueueReferenceFactory queueReferenceFactory,
        RabbitConfiguration rabbitConfiguration,
        IServiceProvider serviceProvider,
        ITypeResolver typeResolver,
        ILogger<RabbitMessageSubscriber> logger,
        ISerializer serializer)
    {
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _rabbitConfiguration = rabbitConfiguration;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    private async ValueTask InitChannelAsync(CancellationToken cancellationToken)
    {
        await StopChannelAsync(cancellationToken);

        _channel = await _channelFactory.GetConsumeChannelAsync(cancellationToken);
        _channel.CallbackExceptionAsync += OnChannelException;
    }

    private Task OnChannelException(object _, CallbackExceptionEventArgs ea)
    {
        _logger.LogError(ea.Exception, "the RabbitMQ Channel has encountered an error: {ExceptionMessage}", ea.Exception.Message);
        return Task.CompletedTask;
    }

    private async ValueTask InitSubscriptionAsync(QueueReferences queueReference, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"initializing subscription on queue '{queueReference.QueueName}' ...");

        await _channel.EnsureTopologyAsync(queueReference, _rabbitConfiguration, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;
        await _channel.BasicConsumeAsync(queue: queueReference.QueueName, autoAck: false, consumer: consumer, cancellationToken);
    }

    private ValueTask StopChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
            _channel.CallbackExceptionAsync -= OnChannelException;
        return ValueTask.CompletedTask;
    }

    //TODO: figure out how to pass a cancellation token
    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        var consumer = sender as IAsyncBasicConsumer;
        var channel = consumer?.Channel ?? _channel;

        if (channel is null)
            throw new InvalidOperationException("Unable to retrieve channel from consumer.");

        MessageEnvelope? message;
        try
        {
            var messageId = eventArgs.BasicProperties.MessageId;
            ArgumentException.ThrowIfNullOrWhiteSpace(messageId, nameof(messageId));

            var correlationId = eventArgs.BasicProperties.CorrelationId;
            ArgumentException.ThrowIfNullOrWhiteSpace(correlationId, nameof(correlationId));

            var messageTypeName = eventArgs.BasicProperties.GetHeaderValue(nameof(message.MessageType));
            ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeName, nameof(messageTypeName));

            var messageType = _typeResolver.Resolve(messageTypeName, throwOnError: true);

            var senderId = eventArgs.BasicProperties.GetHeaderValue(nameof(message.SenderId));
            ArgumentException.ThrowIfNullOrWhiteSpace(senderId, nameof(senderId));

            var createdAt = DateTimeOffset.Parse(eventArgs.BasicProperties.GetHeaderValue(nameof(message.CreatedAt)));

            if (!MessageEnvelope.TryCreate(eventArgs.Body.Span,
                                        messageId: messageId,
                                        correlationId: correlationId,
                                        createdAt,
                                        messageType!,
                                        senderId: senderId,
                                        _serializer,
                                        out message))
                throw new ArgumentException("unable to parse outbox message.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "an exception has occured while decoding queue message from Exchange '{ExchangeName}'. Error: {ExceptionMessage}",
                eventArgs.Exchange, ex.Message);
            await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false);
            return;
        }

        var queueReference = _queueReferenceFactory.Create(message);

        _logger.LogInformation(
            "received message '{MessageId}' from Exchange '{ExchangeName}', Queue '{QueueName}'. Processing...",
            message.MessageId, queueReference.ExchangeName, queueReference.QueueName);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IMessageProcessor>();
            await processor.ProcessAsync(message, CancellationToken.None);

            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
        }
        catch (LockException lockEx)
        {
            await HandleConsumerException(lockEx, eventArgs, channel, queueReference, message, true);
        }
        catch (AggregateException aggEx) when (aggEx.InnerExceptions.Any(ex => ex is LockException))
        {
            await HandleConsumerException(aggEx, eventArgs, channel, queueReference, message, true);
        }
        catch (Exception ex)
        {
            await HandleConsumerException(ex, eventArgs, channel, queueReference, message, false);
        }
    }

    private async ValueTask HandleConsumerException(Exception ex, BasicDeliverEventArgs deliveryProps, IChannel channel, QueueReferences queueReference, MessageEnvelope message, bool requeue)
    {
        var errorMsg = "an error has occurred while processing Message '{MessageId}' from Exchange '{ExchangeName}' : {ExceptionMessage} . "
                     + (requeue ? "Reenqueuing..." : "Nacking...");

        _logger.LogWarning(ex, errorMsg, message.MessageId, queueReference.ExchangeName, ex.Message);

        if (!requeue)
            await channel.BasicRejectAsync(deliveryProps.DeliveryTag, requeue: false);
        else
        {
            // we acknowledge the message so it's removed from the original queue
            await channel.BasicAckAsync(deliveryProps.DeliveryTag, multiple: false);

            var props = new BasicProperties(deliveryProps.BasicProperties);
            // we publish the message to the retry exchange
            await channel.BasicPublishAsync(
                exchange: queueReference.RetryExchangeName,
                routingKey: deliveryProps.RoutingKey,
                mandatory: true,
                basicProperties: props,
                body: deliveryProps.Body);
        }
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await InitChannelAsync(cancellationToken);

        foreach (var queueReference in _queueReferenceFactory.RegisteredQueueReferences)
        {
            await InitSubscriptionAsync(queueReference, cancellationToken);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await StopChannelAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopChannelAsync(CancellationToken.None);
    }
}