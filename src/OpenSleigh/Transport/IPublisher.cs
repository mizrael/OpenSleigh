using OpenSleigh.Outbox;

namespace OpenSleigh.Transport;

public interface IPublisher
{
    ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);
}