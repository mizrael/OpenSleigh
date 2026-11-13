using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class HeldMessagesTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryTakeDue_should_return_the_messages_of_a_partition_in_the_order_they_were_held_once_the_first_is_due()
    {
        var sut = new HeldMessages();
        var first = Message("lorem", 1);
        var second = Message("lorem", 2);
        sut.Hold(first, Now.AddSeconds(10));
        sut.Hold(second, Now);

        Assert.False(sut.TryTakeDue(Now, out _, out _));
        Assert.Equal(TimeSpan.FromSeconds(10), sut.TimeToNextDue(Now));

        Assert.True(sut.TryTakeDue(Now.AddSeconds(10), out var taken, out var isLast));
        Assert.Same(first, taken);
        Assert.False(isLast);
        Assert.True(sut.IsHolding(first.TopicPartition));

        Assert.True(sut.TryTakeDue(Now.AddSeconds(10), out taken, out isLast));
        Assert.Same(second, taken);
        Assert.True(isLast);
        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public void TryTakeDue_should_not_let_a_partition_that_is_waiting_keep_another_one_waiting()
    {
        var sut = new HeldMessages();
        var waiting = Message("lorem", 1);
        var due = Message("ipsum", 1);
        sut.Hold(waiting, Now.AddSeconds(10));
        sut.Hold(due, Now);

        Assert.True(sut.TryTakeDue(Now, out var taken, out _));
        Assert.Same(due, taken);
        Assert.Equal(TimeSpan.FromSeconds(10), sut.TimeToNextDue(Now));
    }

    [Fact]
    public void HoldFirst_should_put_a_message_back_ahead_of_the_ones_held_for_its_partition()
    {
        var sut = new HeldMessages();
        var first = Message("lorem", 1);
        var second = Message("lorem", 2);
        sut.Hold(first, Now);
        sut.Hold(second, Now);
        sut.TryTakeDue(Now, out _, out _);

        sut.HoldFirst(first, Now.AddSeconds(5));

        Assert.False(sut.TryTakeDue(Now, out _, out _));
        Assert.True(sut.TryTakeDue(Now.AddSeconds(5), out var taken, out _));
        Assert.Same(first, taken);
    }

    [Fact]
    public void Forget_should_drop_the_messages_of_that_partition_only()
    {
        var sut = new HeldMessages();
        var forgotten = Message("lorem", 1);
        var kept = Message("ipsum", 1);
        sut.Hold(forgotten, Now);
        sut.Hold(kept, Now);

        Assert.True(sut.Forget(forgotten.TopicPartition));
        Assert.False(sut.Forget(forgotten.TopicPartition));

        Assert.False(sut.IsHolding(forgotten.TopicPartition));
        Assert.True(sut.TryTakeDue(Now, out var taken, out var isLast));
        Assert.Same(kept, taken);
        Assert.True(isLast);
    }

    private static ConsumeResult<string, byte[]> Message(string topic, long offset)
        => new() { Topic = topic, Partition = 0, Offset = offset };
}
