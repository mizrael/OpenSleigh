using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// counts, per message type, the deliveries the hosts processed without an exception, whether a handler ran
/// or the message was ignored. A scenario about messages that must not be handled waits here, not for a time.
/// </summary>
public sealed class Deliveries
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    private readonly ConcurrentDictionary<Type, int> _processed = new();

    public int ProcessedCount<TMessage>()
        where TMessage : IMessage
        => _processed.GetValueOrDefault(typeof(TMessage));

    public async Task WaitUntilProcessedAsync<TMessage>(int times, CancellationToken cancellationToken)
        where TMessage : IMessage
    {
        while (ProcessedCount<TMessage>() < times)
            await Task.Delay(PollingInterval, cancellationToken);
    }

    internal void Add(Type messageType) => _processed.AddOrUpdate(messageType, 1, (_, count) => count + 1);
}

public static class DeliveriesExtensions
{
    /// <summary>
    /// counts what the message processor of a host is done with. One <see cref="Deliveries"/> for all the hosts.
    /// </summary>
    public static IServiceCollection CountDeliveries(this IServiceCollection services, Deliveries deliveries)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        return services.Decorate<IMessageProcessor>((_, inner) => new CountingMessageProcessor(inner, deliveries));
    }

    private sealed class CountingMessageProcessor(IMessageProcessor inner, Deliveries deliveries) : IMessageProcessor
    {
        public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            await inner.ProcessAsync(outboxMessage, cancellationToken);
            deliveries.Add(outboxMessage.MessageType);
        }
    }
}
