using OpenSleigh.Outbox;

namespace OpenSleigh.Transport;

public interface IMessageBus
{
    ValueTask<OutboxAppendResult> PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) where TM : IMessage;
}