namespace OpenSleigh.Outbox;

/// <summary>
/// lets the code that appends messages to the outbox wake up the relay running in the same process,
/// instead of leaving them there until the next poll.
/// </summary>
public interface IOutboxSignal
{
    /// <summary>
    /// signals that new messages are waiting in the outbox.
    /// </summary>
    void NotifyPendingMessages();

    /// <summary>
    /// forgets the signals raised so far. The relay calls it right before reading the outbox:
    /// whatever was signalled until then is about to be read, and what is signalled afterwards is kept.
    /// </summary>
    void ClearPendingMessages();

    /// <summary>
    /// completes when new messages are signalled or when the timeout elapses, whichever comes first.
    /// It completes at once when a signal was raised and not cleared since.
    /// </summary>
    /// <returns>true when woken up by a signal, false on timeout.</returns>
    ValueTask<bool> WaitForPendingMessagesAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
