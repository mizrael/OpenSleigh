namespace OpenSleigh.Outbox
{
    public interface IOutboxProcessor
    {
        ValueTask ProcessPendingMessagesAsync(CancellationToken cancellationToken = default);
    }
}