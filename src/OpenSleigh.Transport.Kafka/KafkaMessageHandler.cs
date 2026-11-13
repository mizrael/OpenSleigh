using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using System.Text;

namespace OpenSleigh.Transport.Kafka;

public class KafkaMessageHandler : IKafkaMessageHandler
{
    private readonly IKafkaMessageParser _messageParser;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKafkaPublisherExecutor _publisher;
    private readonly ILogger<KafkaMessageHandler> _logger;
    private readonly ISystemInfo _systemInfo;
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly TimeSpan _retryDelay;

    /// <summary>
    /// creates a handler that processes every message in its own dependency injection scope,
    /// with its own <see cref="IMessageProcessor"/>.
    /// </summary>
    /// <param name="configuration">its retry delay is how long a message waits after a lock conflict. Defaults to 30 seconds.</param>
    public KafkaMessageHandler(IKafkaMessageParser messageParser,
                                IServiceScopeFactory scopeFactory,
                                IKafkaPublisherExecutor publisher,
                                ILogger<KafkaMessageHandler> logger,
                                ISystemInfo systemInfo,
                                IQueueReferenceFactory queueReferenceFactory,
                                KafkaConfiguration? configuration = null)
    {
        _retryDelay = configuration?.RetryDelay ?? KafkaConfiguration.DefaultRetryDelay;
        _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
    }

    private async ValueTask ProcessInOwnScopeAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var messageProcessor = scope.ServiceProvider.GetRequiredService<IMessageProcessor>();
        await messageProcessor.ProcessAsync(message, cancellationToken);
    }

    /// <returns>
    /// true when the message was dealt with and must not be delivered again: it was processed, published to its retry
    /// or dead letter topic, or it cannot be read at all. False when it still has to be processed: the consumer is
    /// stopping, or the message could not be published to the topic it had to go to.
    /// </returns>
    public async ValueTask<bool> HandleAsync(ConsumeResult<string, byte[]> result, CancellationToken cancellationToken = default)
    {
        MessageEnvelope message;
        try
        {
            message = _messageParser.Parse(result);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'", result.Topic);
            return false;
        }
        catch (Exception ex)
        {
            // delivered again it would fail again: it is left behind
            _logger.LogError(ex, "a message from Topic '{Topic}' cannot be read and is skipped: {Exception}",
                result.Topic, ex.Message);
            return true;
        }

        var queueReferences = _queueReferenceFactory.Create(message);

        return await HandleCoreAsync(message, queueReferences, cancellationToken);
    }

    private async ValueTask<bool> HandleCoreAsync(MessageEnvelope message, QueueReferences queueReferences, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "client {ClientGroup}/{ClientId} received message '{MessageId}' from Topic '{Topic}'. Processing...",
            _systemInfo.ClientGroup, _systemInfo.ClientId,
            message.MessageId, queueReferences.TopicName);
        try
        {
            await ProcessInOwnScopeAsync(message, cancellationToken);
            return true;
        }
        // interrupted by a stop: the message is delivered again. Any other cancellation, a timeout for instance, is a failure
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            return await HandleProcessErrors(message, queueReferences, ex, cancellationToken);
        }
    }

    private ValueTask<bool> HandleProcessErrors(MessageEnvelope message, QueueReferences queueReferences, Exception ex,
                                                 CancellationToken cancellationToken)
    {
        _logger.LogWarning(ex, "an exception has occurred while consuming message '{MessageId}': {Exception}",
                           message.MessageId, ex.Message);

        return ex.IsLockConflict() && !string.IsNullOrWhiteSpace(queueReferences.RetryTopicName)
            ? PublishToRetryTopicAsync(message, queueReferences, ex, cancellationToken)
            : PublishToDLQAsync(message, queueReferences, ex);
    }

    private async ValueTask<bool> PublishToRetryTopicAsync(MessageEnvelope message, QueueReferences queueReferences, Exception ex,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("pushing message '{MessageId}' to retry topic '{RetryTopic}' ...",
                message.MessageId, queueReferences.RetryTopicName);

            // not cancellable, like the publish to the dead letter topic: cancelled in flight it can still
            // be delivered, and the message would then be both there and delivered again
            await _publisher.PublishAsync(message, queueReferences.RetryTopicName,
                additionalHeaders:
                [
                    RetrySchedule.CreateHeader(DateTimeOffset.UtcNow + _retryDelay),
                    RetryTopics.CreateGroupHeader(_systemInfo.ClientGroup)
                ],
                cancellationToken: CancellationToken.None);
            return true;
        }
        catch (Exception retryEx)
        {
            _logger.LogWarning(retryEx,
                "an exception has occurred while publishing message '{MessageId}' to retry topic '{RetryTopic}': {Exception}",
                message.MessageId,
                queueReferences.RetryTopicName,
                retryEx.Message);

            // a consumer that is stopping leaves the message where it is, to be delivered again
            if (cancellationToken.IsCancellationRequested)
                return false;

            // the message cannot wait for its retry: it is kept where it can be found, instead of being lost
            return await PublishToDLQAsync(message, queueReferences, ex);
        }
    }

    private async ValueTask<bool> PublishToDLQAsync(MessageEnvelope message, QueueReferences queueReferences, Exception ex)
    {
        if (string.IsNullOrWhiteSpace(queueReferences.DeadLetterTopicName))
            return true;

        try
        {
            _logger.LogWarning("pushing message '{MessageId}' to DLQ '{DeadLetterQueue}' ...",
                message.MessageId, queueReferences.DeadLetterTopicName);

            await _publisher.PublishAsync(message, queueReferences.DeadLetterTopicName,
                additionalHeaders:
                [
                    new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message))
                ],
                cancellationToken: CancellationToken.None);
            return true;
        }
        catch (Exception dlqEx)
        {
            _logger.LogWarning(dlqEx,
                "an exception has occurred while publishing message '{MessageId}' to DLQ '{DeadLetterQueue}': {Exception}",
                message.MessageId,
                queueReferences.DeadLetterTopicName,
                dlqEx.Message);
            return false;
        }
    }
}
