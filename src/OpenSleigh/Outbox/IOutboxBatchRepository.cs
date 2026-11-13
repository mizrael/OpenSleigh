namespace OpenSleigh.Outbox;

/// <summary>
/// implemented by an outbox repository that can remove the messages of a relay pass with one call.
/// The relay uses it when the repository has it, and removes the messages one by one otherwise.
/// </summary>
/// <remarks>
/// The messages are removed after all of them were published, so it is meant for repositories whose relay pass
/// is one transaction: there, a pass that stops half way already leaves every message it read in the outbox.
/// </remarks>
public interface IOutboxBatchRepository
{
    /// <summary>
    /// removes the messages from the outbox.
    /// </summary>
    ValueTask DeleteAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default);
}
