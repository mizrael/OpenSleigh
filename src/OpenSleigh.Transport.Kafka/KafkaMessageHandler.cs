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
    private readonly KafkaConfiguration _configuration;

    /// <summary>
    /// creates a handler that processes every message in its own dependency injection scope,
    /// with its own <see cref="IMessageProcessor"/>.
    /// </summary>
    public KafkaMessageHandler(IKafkaMessageParser messageParser,
                                IServiceScopeFactory scopeFactory,
                                IKafkaPublisherExecutor publisher,
                                ILogger<KafkaMessageHandler> logger,
                                ISystemInfo systemInfo,
                                IQueueReferenceFactory queueReferenceFactory,
                                KafkaConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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

    public async ValueTask<bool> HandleAsync(ConsumeResult<string, byte[]> result, CancellationToken cancellationToken = default)
    {
        MessageEnvelope message;
        try
        {
            message = _messageParser.Parse(result);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "consumer closed on Topic '{Topic}', probably during Dispose() call", result.Topic);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'", result.Topic);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'", result.Topic);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "an error has occurred while consuming messages from Topic '{Topic}': {Exception}",
                result.Topic, ex.Message);
            return false;
        }

        var queueReferences = _queueReferenceFactory.Create(message);

        await HandleCoreAsync(message, queueReferences, cancellationToken);
        return true;
    }

    private async ValueTask HandleCoreAsync(MessageEnvelope message, QueueReferences queueReferences, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "client {ClientGroup}/{ClientId} received message '{MessageId}' from Topic '{Topic}'. Processing...",
            _systemInfo.ClientGroup, _systemInfo.ClientId,
            message.MessageId, queueReferences.TopicName);
        try
        {
            await ProcessInOwnScopeAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            await HandleProcessErrors(message, queueReferences, ex, cancellationToken);
        }
    }

    private ValueTask HandleProcessErrors(MessageEnvelope message, QueueReferences queueReferences, Exception ex,
                                            CancellationToken cancellationToken)
    {
        _logger.LogWarning(ex, "an exception has occurred while consuming message '{MessageId}': {Exception}",
                           message.MessageId, ex.Message);

        return ex.IsLockConflict() && !string.IsNullOrWhiteSpace(queueReferences.RetryTopicName)
            ? PublishToRetryTopicAsync(message, queueReferences, ex, cancellationToken)
            : PublishToDLQAsync(message, queueReferences, ex, cancellationToken);
    }

    private async ValueTask PublishToRetryTopicAsync(MessageEnvelope message, QueueReferences queueReferences, Exception ex,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("pushing message '{MessageId}' to retry topic '{RetryTopic}' ...",
                message.MessageId, queueReferences.RetryTopicName);

            await _publisher.PublishAsync(message, queueReferences.RetryTopicName,
                additionalHeaders:
                [
                    RetrySchedule.CreateHeader(DateTimeOffset.UtcNow + _configuration.RetryDelay)
                ],
                cancellationToken: cancellationToken);
        }
        catch (Exception retryEx)
        {
            _logger.LogWarning(retryEx,
                "an exception has occurred while publishing message '{MessageId}' to retry topic '{RetryTopic}': {Exception}",
                message.MessageId,
                queueReferences.RetryTopicName,
                retryEx.Message);

            // the message cannot wait for its retry: it is kept where it can be found, instead of being lost
            await PublishToDLQAsync(message, queueReferences, ex, cancellationToken);
        }
    }

    private async ValueTask PublishToDLQAsync(MessageEnvelope message, QueueReferences queueReferences, Exception ex,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queueReferences.DeadLetterTopicName))
            return;

        try
        {
            _logger.LogWarning("pushing message '{MessageId}' to DLQ '{DeadLetterQueue}' ...",
                message.MessageId, queueReferences.DeadLetterTopicName);

            await _publisher.PublishAsync(message, queueReferences.DeadLetterTopicName,
                additionalHeaders:
                [
                    new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message))
                ],
                cancellationToken: cancellationToken);
        }
        catch (Exception dlqEx)
        {
            _logger.LogWarning(dlqEx,
                "an exception has occurred while publishing message '{MessageId}' to DLQ '{DeadLetterQueue}': {Exception}",
                message.MessageId,
                queueReferences.DeadLetterTopicName,
                dlqEx.Message);
        }
    }
}
