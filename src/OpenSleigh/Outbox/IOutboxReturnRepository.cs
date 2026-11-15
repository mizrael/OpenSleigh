namespace OpenSleigh.Outbox;

/// <summary>
/// implemented by an <see cref="IOutboxRepository"/> that can take back messages a transport returned
/// after the relay published them. Without it, a returned message is lost.
/// </summary>
public interface IOutboxReturnRepository
{
    /// <summary>
    /// stores the messages again, with the <see cref="MessageEnvelope.DeliverAt"/> and
    /// <see cref="MessageEnvelope.FirstReturnedAt"/> they come with, replacing the ones that have the same id.
    /// </summary>
    /// <remarks>
    /// A message is returned some time after it was published: the relay may not have deleted it yet.
    /// The message that is stored here must not be removed by that delete, whichever of the two comes first.
    /// </remarks>
    ValueTask PutBackAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default);
}
