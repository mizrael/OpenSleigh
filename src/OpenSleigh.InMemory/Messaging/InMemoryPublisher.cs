using Microsoft.Extensions.Logging;
using OpenSleigh.Messaging;
using OpenSleigh.Outbox;
using System.Threading.Channels;

namespace OpenSleigh.InMemory.Messaging
{
    public class InMemoryPublisher : IPublisher
    {
        private readonly ChannelWriter<OutboxMessage> _writer;
        private readonly ILogger<InMemoryPublisher> _logger;

        public InMemoryPublisher(ChannelWriter<OutboxMessage> writer, ILogger<InMemoryPublisher> logger)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return PublishAsyncCore((dynamic)message, cancellationToken);
        }

        private async ValueTask PublishAsyncCore(OutboxMessage message, CancellationToken cancellationToken)            
        {
            _logger.LogInformation(
                    "publishing message '{MessageType}/{MessageId}'...",
                    message.MessageType.FullName,
                    message.MessageId);

            await _writer.WriteAsync(message, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}