namespace OpenSleigh.Outbox;

/// <summary>
/// takes back a message a transport returned after the relay published it. A store registers it as a service
/// of its own, next to its <see cref="IOutboxRepository"/>: without it, a returned message is lost.
/// </summary>
public interface IOutboxReturnRepository
{
    /// <summary>
    /// stores the message again, to be published at its <see cref="MessageEnvelope.DeliverAt"/>,
    /// replacing the one that has the same id.
    /// </summary>
    /// <param name="message">
    /// as <see cref="MessageEnvelope.Returned"/> creates it: with a <see cref="MessageEnvelope.DeliverAt"/>
    /// and a <see cref="MessageEnvelope.FirstReturnedAt"/> that is not earlier than its creation.
    /// </param>
    /// <remarks>
    /// <para>A message is returned some time after it was published: the relay may not have deleted it yet.
    /// The message that is stored here must not be removed by that delete, whichever of the two comes first.</para>
    /// <para>Several hosts can put the same message back, when each of them published it: the earliest
    /// <see cref="MessageEnvelope.FirstReturnedAt"/> is the one that is kept, unless the one that is stored
    /// is earlier than the creation of the message.</para>
    /// <para><see cref="IOutboxRepository.ReadPendingAsync"/> returns the message with that time.</para>
    /// </remarks>
    ValueTask PutBackAsync(MessageEnvelope message, CancellationToken cancellationToken = default);
}
