using OpenSleigh.Outbox;

namespace OpenSleigh.Transport;

public interface IPublisher
{
    ValueTask PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default);
}