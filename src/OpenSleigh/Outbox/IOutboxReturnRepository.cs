namespace OpenSleigh.Outbox;

/// <summary>
/// takes back the messages a transport returned after the relay published them. A store registers it as a service
/// of its own, next to its <see cref="IOutboxRepository"/>: without it, a returned message is lost.
/// </summary>
public interface IOutboxReturnRepository
{
    /// <summary>
    /// stores the messages again, to be published at their <see cref="MessageEnvelope.DeliverAt"/>,
    /// replacing the ones that have the same id.
    /// </summary>
    /// <param name="messages">
    /// each with an id of its own, a <see cref="MessageEnvelope.DeliverAt"/> and a <see cref="MessageEnvelope.FirstReturnedAt"/>,
    /// as <see cref="MessageEnvelope.Returned"/> creates them. <see cref="ThrowIfNotReturned"/> checks it.
    /// </param>
    /// <remarks>
    /// <para>A message is returned some time after it was published: the relay may not have deleted it yet.
    /// The message that is stored here must not be removed by that delete, whichever of the two comes first.</para>
    /// <para>Several hosts can put the same message back at the same time, when each of them published it:
    /// none of them fails for it, and the earliest <see cref="MessageEnvelope.FirstReturnedAt"/> is the one that is kept.</para>
    /// <para><see cref="IOutboxRepository.ReadPendingAsync"/> returns the message with that time.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">a message has no due time or no time of first return, or two have the same id.</exception>
    ValueTask PutBackAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// what every implementation requires of the messages it is given.
    /// </summary>
    static void ThrowIfNotReturned(IReadOnlyCollection<MessageEnvelope> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var ids = new HashSet<string>(messages.Count);
        foreach (var message in messages)
        {
            if (message.DeliverAt is null || message.FirstReturnedAt is null)
                throw new ArgumentException($"message '{message.MessageId}' has no due time, or no time of first return.", nameof(messages));

            if (!ids.Add(message.MessageId))
                throw new ArgumentException($"message '{message.MessageId}' is there twice.", nameof(messages));
        }
    }
}
