using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

public static class DeliveryObservationExtensions
{
    /// <summary>
    /// tells a scenario when a host is done with a message of a type without failing, whatever it did with it:
    /// a message that no saga handles, because its saga is completed, reaches no handler that could report it.
    /// A delivery that fails is in <see cref="ScenarioRun.ProcessingFailures"/>.
    /// </summary>
    public static IServiceCollection ObserveDeliveriesOf<TMessage>(this IServiceCollection services, Expectation processed)
        where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(processed);
        return services.Decorate<IMessageProcessor>((_, inner) => new ObservingMessageProcessor(inner, typeof(TMessage), processed));
    }

    /// <summary>
    /// tells a scenario about every message the relay of a host published.
    /// </summary>
    public static IServiceCollection ObservePublishedMessages(this IServiceCollection services, Action<MessageEnvelope> onPublished)
    {
        ArgumentNullException.ThrowIfNull(onPublished);
        return services.Decorate<IPublisher>((_, inner) => new ObservingPublisher(inner, onPublished));
    }

    /// <summary>
    /// tells a scenario about every pass of the relay of a host that left something in the outbox, or could not
    /// read it at all.
    /// </summary>
    public static IServiceCollection ObserveFailedRelayPasses(this IServiceCollection services, Action onFailedPass)
    {
        ArgumentNullException.ThrowIfNull(onFailedPass);
        return services.Decorate<IOutboxProcessor>((_, inner) => new ObservingOutboxProcessor(inner, onFailedPass));
    }

    private sealed class ObservingOutboxProcessor(IOutboxProcessor inner, Action onFailedPass) : IOutboxProcessor
    {
        public ValueTask ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
            => inner.ProcessPendingMessagesAsync(cancellationToken);

        public async ValueTask<OutboxRelayResult> RelayPendingMessagesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await inner.RelayPendingMessagesAsync(cancellationToken);
                if (result.Failed > 0)
                    onFailedPass();

                return result;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                onFailedPass();
                throw;
            }
        }
    }

    private sealed class ObservingPublisher(IPublisher inner, Action<MessageEnvelope> onPublished) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            await inner.PublishAsync(envelope, cancellationToken);
            onPublished(envelope);
        }
    }

    private sealed class ObservingMessageProcessor(IMessageProcessor inner, Type messageType, Expectation processed) : IMessageProcessor
    {
        public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            await inner.ProcessAsync(outboxMessage, cancellationToken);

            if (outboxMessage.MessageType == messageType)
                processed.Signal();
        }
    }
}
