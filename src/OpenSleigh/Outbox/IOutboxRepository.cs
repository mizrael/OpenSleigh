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

    ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default);
}