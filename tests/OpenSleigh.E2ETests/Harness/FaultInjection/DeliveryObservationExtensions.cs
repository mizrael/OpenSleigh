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
    /// tells a scenario about every pass of the relay of a host: which host, and whether the pass left something
    /// in the outbox or could not read it at all.
    /// </summary>
    /// <param name="onPass">given the host, the same object for every pass of one host, and true for a pass that failed.</param>
    public static IServiceCollection ObserveRelayPasses(this IServiceCollection services, Action<object, bool> onPass)
    {
        ArgumentNullException.ThrowIfNull(onPass);
        return services.Decorate<IOutboxProcessor>((provider, inner)
            => new ObservingOutboxProcessor(inner, provider.GetRequiredService<ISystemInfo>(), onPass));
    }

    /// <summary>
    /// tells a scenario about every message a host gave back to its transport, and why.
    /// Unlike <see cref="ScenarioRun.ProcessingFailures"/>, it comes with the message, and so with its correlation id.
    /// </summary>
    public static IServiceCollection ObserveFailedDeliveries(this IServiceCollection services, Action<MessageEnvelope, Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onFailure);
        return services.Decorate<IMessageProcessor>((_, inner) => new FailureObservingMessageProcessor(inner, onFailure));
    }

    private sealed class FailureObservingMessageProcessor(IMessageProcessor inner, Action<MessageEnvelope, Exception> onFailure) : IMessageProcessor
    {
        public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                await inner.ProcessAsync(outboxMessage, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                onFailure(outboxMessage, e);
                throw;
            }
        }
    }

    private sealed class ObservingOutboxProcessor(IOutboxProcessor inner, object host, Action<object, bool> onPass) : IOutboxProcessor
    {
        public ValueTask ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
            => inner.ProcessPendingMessagesAsync(cancellationToken);

        public async ValueTask<OutboxRelayResult> RelayPendingMessagesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await inner.RelayPendingMessagesAsync(cancellationToken);
                onPass(host, result.Failed > 0);

                return result;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                onPass(host, true);
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
