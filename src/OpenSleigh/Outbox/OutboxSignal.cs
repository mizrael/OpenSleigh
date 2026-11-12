namespace OpenSleigh.Outbox;

/// <summary>
/// a signal that is either raised or not: raising it any number of times before it is cleared wakes the relay once.
/// </summary>
public sealed class OutboxSignal : IOutboxSignal
{
    private TaskCompletionSource _pending = NewSource();

    public void NotifyPendingMessages()
        => Volatile.Read(ref _pending).TrySetResult();

    public void ClearPendingMessages()
    {
        // a notification that races with this and lands on the source being replaced is not lost:
        // its message was appended before the read that follows this call
        var current = Volatile.Read(ref _pending);
        if (current.Task.IsCompleted)
            Interlocked.CompareExchange(ref _pending, NewSource(), current);
    }

    public async ValueTask<bool> WaitForPendingMessagesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            await Volatile.Read(ref _pending).Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static TaskCompletionSource NewSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
