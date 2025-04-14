using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.Threading.Channels;

namespace OpenSleigh.InMemory.Messaging;

public class InMemoryPublisher : IPublisher
{
    private readonly ChannelWriter<MessageEnvelope> _writer;
    private readonly ILogger<InMemoryPublisher> _logger;

    public InMemoryPublisher(ChannelWriter<MessageEnvelope> writer, ILogger<InMemoryPublisher> logger)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return PublishAsyncCore((dynamic)message, cancellationToken);
    }

    private async ValueTask PublishAsyncCore(MessageEnvelope message, CancellationToken cancellationToken)            
    {
        _logger.LogInformation(
                "publishing message '{MessageType}/{MessageId}'...",
                message.MessageType.FullName,
                message.MessageId);

        await _writer.WriteAsync(message, cancellationToken)
            .ConfigureAwait(false);
    }
}