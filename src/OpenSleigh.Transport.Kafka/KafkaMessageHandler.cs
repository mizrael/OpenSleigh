using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using System.Text;

namespace OpenSleigh.Transport.Kafka;

public class KafkaMessageHandler : IKafkaMessageHandler
{
    private readonly IMessageParser _messageParser;
    private readonly IMessageProcessor _messageProcessor;
    private readonly IKafkaPublisherExecutor _publisher;
    private readonly ILogger<KafkaMessageHandler> _logger;
    private readonly ISystemInfo _systemInfo;
    private readonly IQueueReferenceFactory _queueReferenceFactory;

    public KafkaMessageHandler(IMessageParser messageParser,
                                IMessageProcessor messageProcessor,
                                IKafkaPublisherExecutor publisher,
                                ILogger<KafkaMessageHandler> logger,
                                ISystemInfo systemInfo,
                                IQueueReferenceFactory queueReferenceFactory)
    {
        _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
        _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
    }

    public async ValueTask<bool> HandleAsync(ConsumeResult<string, byte[]> result, CancellationToken cancellationToken = default)
    {
        var queueReferences = _queueReferenceFactory.Get(result.Topic);
        if (queueReferences is null)
        {
            _logger.LogWarning("no queue references found for topic '{Topic}'", result.Topic);
            return false;
        }

        MessageEnvelope? message = null;

        try
        {
            message = _messageParser.Parse(result);
        }
        catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
        {
            // noop. seems to be a known issue in the c# Kafka driver
            // occurring when consumers are started before producers.

            _logger.LogWarning(ex, "Topic '{Topic}' still not available : {Exception}",
                queueReferences.TopicName, ex.Message);
            return true;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "consumer closed on Topic '{Topic}', probably during Dispose() call",
                queueReferences.TopicName);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'",
                queueReferences.TopicName);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'",
                queueReferences.TopicName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "an error has occurred while consuming messages from Topic '{Topic}': {Exception}",
                queueReferences.TopicName, ex.Message);
        }

        if (message is null)
            return false;

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
            await _messageProcessor.ProcessAsync(message, cancellationToken);
        }
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

        return PublishToDLQAsync(message, queueReferences, ex, cancellationToken);
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
