using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka;

/// <summary>
/// the messages a consumer has received before they can be processed, by partition and in the order they arrived.
/// Used by the consume loop only: not thread safe.
/// </summary>
internal sealed class HeldMessages
{
    private readonly Dictionary<TopicPartition, LinkedList<Held>> _partitions = new();

    public bool IsEmpty => _partitions.Count == 0;

    public bool IsHolding(TopicPartition partition) => _partitions.ContainsKey(partition);

    /// <summary>
    /// keeps a message behind the ones already held for its partition.
    /// </summary>
    public void Hold(ConsumeResult<string, byte[]> message, DateTimeOffset dueAt)
        => MessagesOf(message.TopicPartition).AddLast(new Held(message, dueAt));

    /// <summary>
    /// keeps a message ahead of the ones already held for its partition: it was taken and has to be processed again.
    /// </summary>
    public void HoldFirst(ConsumeResult<string, byte[]> message, DateTimeOffset dueAt)
        => MessagesOf(message.TopicPartition).AddFirst(new Held(message, dueAt));

    private LinkedList<Held> MessagesOf(TopicPartition partition)
    {
        if (!_partitions.TryGetValue(partition, out var held))
            _partitions[partition] = held = new LinkedList<Held>();

        return held;
    }

    /// <summary>
    /// takes the first message of a partition whose turn has come.
    /// </summary>
    /// <param name="isLastOfPartition">true when nothing else is held for the partition of the message.</param>
    public bool TryTakeDue(DateTimeOffset now, out ConsumeResult<string, byte[]> message, out bool isLastOfPartition)
    {
        foreach (var (partition, held) in _partitions)
        {
            var first = held.First!.Value;
            if (first.DueAt > now)
                continue;

            held.RemoveFirst();
            message = first.Message;
            isLastOfPartition = held.Count == 0;
            if (isLastOfPartition)
                _partitions.Remove(partition);
            return true;
        }

        message = null!;
        isLastOfPartition = false;
        return false;
    }

    public TimeSpan TimeToNextDue(DateTimeOffset now)
    {
        var nextDue = DateTimeOffset.MaxValue;
        foreach (var held in _partitions.Values)
        {
            var dueAt = held.First!.Value.DueAt;
            if (dueAt < nextDue)
                nextDue = dueAt;
        }

        return nextDue > now ? nextDue - now : TimeSpan.Zero;
    }

    /// <summary>
    /// forgets the messages of a partition that this consumer no longer owns: its next owner receives them.
    /// </summary>
    /// <returns>true when messages were held for the partition.</returns>
    public bool Forget(TopicPartition partition) => _partitions.Remove(partition);

    private readonly record struct Held(ConsumeResult<string, byte[]> Message, DateTimeOffset DueAt);
}
