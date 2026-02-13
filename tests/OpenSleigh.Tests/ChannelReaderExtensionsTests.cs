using OpenSleigh.InMemory.Messaging;
using OpenSleigh.Outbox;
using System.Threading.Channels;

namespace OpenSleigh.Tests;

public class ChannelReaderExtensionsTests
{
    [Fact]
    public async Task ReadMultipleAsync_should_read_up_to_maxBatchSize()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var envelope1 = DummyMessage.CreateEnvelope();
        var envelope2 = DummyMessage.CreateEnvelope();
        var envelope3 = DummyMessage.CreateEnvelope();

        await channel.Writer.WriteAsync(envelope1);
        await channel.Writer.WriteAsync(envelope2);
        await channel.Writer.WriteAsync(envelope3);

        var batch = await channel.Reader.ReadMultipleAsync(2, CancellationToken.None);

        Assert.Equal(2, batch.Count());
    }

    [Fact]
    public async Task ReadMultipleAsync_should_return_available_when_less_than_batch_size()
    {
        var channel = Channel.CreateUnbounded<MessageEnvelope>();
        var envelope = DummyMessage.CreateEnvelope();

        await channel.Writer.WriteAsync(envelope);

        var batch = await channel.Reader.ReadMultipleAsync(10, CancellationToken.None);

        Assert.Single(batch);
    }
}
