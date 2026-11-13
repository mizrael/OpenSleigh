using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka;

/// <summary>
/// the messages a consumer has received before they can be processed, by partition and in the order they arrived.
/// Used by the consume loop only: not thread safe.
/// </summary>
internal sealed class HeldMessages
{
    private readonly Dictionary<TopicPartition, Queue<Held>> _partitions = new();

    public bool IsEmpty => _partitions.Count == 0;

    public bool IsHolding(TopicPartition partition) => _partitions.ContainsKey(partition);

    public void Hold(ConsumeResult<string, byte[]> message, DateTimeOffset dueAt)
    {
        var partition = message.TopicPartition;
        if (!_partitions.TryGetValue(partition, out var held))
            _partitions[partition] = held = new Queue<Held>();

        held.Enqueue(new Held(message, dueAt));
    }

    /// <summary>
    /// takes the first message of a partition whose turn has come.
    /// </summary>
    /// <param name="isLastOfPartition">true when nothing else is held for the partition of the message.</param>
    public bool TryTakeDue(DateTimeOffset now, out ConsumeResult<string, byte[]> message, out bool isLastOfPartition)
    {
        foreach (var (partition, held) in _partitions)
        {
            if (held.Peek().DueAt > now)
                continue;

            message = held.Dequeue().Message;
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
            var dueAt = held.Peek().DueAt;
            if (dueAt < nextDue)
                nextDue = dueAt;
        }

        return nextDue > now ? nextDue - now : TimeSpan.Zero;
    }

    private readonly record struct Held(ConsumeResult<string, byte[]> Message, DateTimeOffset DueAt);
}
