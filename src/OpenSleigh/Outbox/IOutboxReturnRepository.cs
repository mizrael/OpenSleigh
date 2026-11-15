namespace OpenSleigh.Outbox;

/// <summary>
/// implemented by an <see cref="IOutboxRepository"/> that can take back messages a transport returned
/// after the relay published them. Without it, a returned message is lost.
/// </summary>
public interface IOutboxReturnRepository
{
    /// <summary>
    /// stores the messages again, with the <see cref="MessageEnvelope.DeliverAt"/> they come with,
    /// replacing the ones that have the same id.
    /// </summary>
    /// <remarks>
    /// <para>A message is returned some time after it was published: the relay may not have deleted it yet.
    /// The message that is stored here must not be removed by that delete, whichever of the two comes first.</para>
    /// <para>Several hosts can put the same message back at the same time, when each of them published it:
    /// none of them fails for it, and the earliest <see cref="MessageEnvelope.FirstReturnedAt"/> is the one that is kept.</para>
    /// </remarks>
    ValueTask PutBackAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default);
}
