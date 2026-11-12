namespace OpenSleigh.Outbox;

public interface IOutboxProcessor
{
    ValueTask ProcessPendingMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// relays the pending outbox messages and reports how many were published and removed from the outbox,
    /// and how many were left there.
    /// </summary>
    async ValueTask<OutboxRelayResult> RelayPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        await ProcessPendingMessagesAsync(cancellationToken).ConfigureAwait(false);
        return OutboxRelayResult.Nothing;
    }
}