namespace OpenSleigh.Outbox;

public enum OutboxAppendResult
{
    Undefined,
    Success,
    Duplicate
}

public interface IOutboxRepository
{
    /// <summary>
    /// appends a new message to the outbox and returns <see cref="OutboxAppendResult.Success"/>.
    /// In case of duplicate messages, <see cref="OutboxAppendResult.Duplicate"/> will be returned
    /// and no message will be appended.
    /// </summary>
    /// <param name="messages">the messages to append.</param>
    /// <param name="cancellationToken">the cancellation token.</param>
    /// <returns>an <see cref="OutboxAppendResult"/>.</returns>
    ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// returns messages for the relay to publish. It should only return the ones that are due:
    /// <see cref="MessageEnvelope.DeliverAt"/> is null, or not later than the current time in UTC.
    /// The relay does not publish a message that is not due and leaves it in the outbox,
    /// but a repository that returns such messages makes every pass read them again until they are.
    /// </summary>
    /// <param name="cancellationToken">the cancellation token.</param>
    /// <returns>the messages that are due.</returns>
    ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default);
}