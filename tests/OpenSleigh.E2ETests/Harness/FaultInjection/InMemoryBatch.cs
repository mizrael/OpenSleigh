using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Outbox;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// an in-memory host reads the messages that wait for it as a batch, handles the batch at the same time, and reads
/// again when it is done: two messages meet only if they are in the same batch, and a message that arrives alone
/// is a batch of one. This keeps the host from reading until as many messages wait as the scenario asks for,
/// so that they are read together. It does nothing on the other transports.
/// </summary>
public sealed class InMemoryBatch
{
    /// <summary>how many messages an in-memory host reads at once, and how many can wait for it.</summary>
    public static int MaxSize => OpenSleigh.InMemory.InMemorySagaOptions.Defaults.SubscriberMaxMessagesBatchSize;

    private const int Unrestricted = 1;
    private int _size = Unrestricted;

    internal int Size => Volatile.Read(ref _size);

    /// <summary>until the returned object is disposed, the host reads nothing while fewer messages wait.</summary>
    public IDisposable Of(int messages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(messages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(messages, MaxSize);

        Volatile.Write(ref _size, messages);
        return new Restriction(this);
    }

    private sealed class Restriction(InMemoryBatch batch) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref batch._size, Unrestricted);
    }
}

public static class InMemoryBatchExtensions
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(5);

    public static IServiceCollection ReadInMemoryMessagesIn(this IServiceCollection services, InMemoryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var isInMemory = services.Any(descriptor => descriptor.ServiceType == typeof(ChannelReader<MessageEnvelope>));
        return isInMemory
            ? services.Decorate<ChannelReader<MessageEnvelope>>((_, inner) => new BatchingReader(inner, batch))
            : services;
    }

    private sealed class BatchingReader(ChannelReader<MessageEnvelope> inner, InMemoryBatch batch) : ChannelReader<MessageEnvelope>
    {
        public override Task Completion => inner.Completion;

        public override bool CanCount => inner.CanCount;

        public override int Count => inner.Count;

        public override bool TryRead(out MessageEnvelope item) => inner.TryRead(out item!);

        public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            while (inner.Count < batch.Size)
                await Task.Delay(PollingInterval, cancellationToken);

            return await inner.WaitToReadAsync(cancellationToken);
        }
    }
}
