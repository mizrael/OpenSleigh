namespace OpenSleigh.Outbox;

public sealed class OutboxSignal : IOutboxSignal, IDisposable
{
    private readonly SemaphoreSlim _pending = new(0);

    public void NotifyPendingMessages()
    {
        // one pending signal is enough: the relay reads everything that is waiting.
        // A concurrent caller may add a second one, which only costs an extra read.
        if (_pending.CurrentCount == 0)
            _pending.Release();
    }

    public async ValueTask<bool> WaitForPendingMessagesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => await _pending.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

    public void Dispose() => _pending.Dispose();
}
