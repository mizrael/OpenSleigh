using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// makes the infrastructure of a host fail while a <see cref="Fault"/> is on: the broker cannot be published to,
/// the outbox cannot be read or cleaned, a saga cannot be committed. Call them from <see cref="HostSetup.ConfigureServices"/>.
/// What fails is reported to the fault with the host as its source.
/// </summary>
public static class InfrastructureFaultExtensions
{
    /// <summary>
    /// the relay of the host cannot publish while the fault is on.
    /// </summary>
    public static IServiceCollection FailPublishingWhile(this IServiceCollection services, Fault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        return services.Decorate<IPublisher>((provider, inner) => new FaultyPublisher(inner, fault, HostOf(provider)));
    }

    /// <summary>the relay of the host cannot read its outbox while the fault is on.</summary>
    public static IServiceCollection FailOutboxReadsWhile(this IServiceCollection services, Fault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        return services.Intercept(provider => new FaultyOutbox(HostOf(provider)) { Reads = fault });
    }

    /// <summary>the relay of the host cannot remove what it published from its outbox while the fault is on.</summary>
    public static IServiceCollection FailOutboxDeletesWhile(this IServiceCollection services, Fault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        return services.Intercept(provider => new FaultyOutbox(HostOf(provider)) { Deletes = fault });
    }

    /// <summary>
    /// while the fault is on, a saga of the host that published a <typeparamref name="TMessage"/> cannot be committed:
    /// neither its state nor its messages are stored. Whatever else the host does with its store works.
    /// </summary>
    public static IServiceCollection FailCommitsThatPublish<TMessage>(this IServiceCollection services, Fault fault)
        where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(fault);

        // a store either commits a saga and its messages together, or appends the messages to the outbox
        // and then releases the saga: the commit of a saga fails at the first thing it does, whichever it is
        return services
            .Intercept(provider => new FaultyCommit(fault, typeof(TMessage), HostOf(provider)))
            .Intercept(provider => new FaultyOutbox(HostOf(provider)) { Appends = fault, AppendedMessageType = typeof(TMessage) });
    }

    // one per host, whatever scope the provider is
    private static object HostOf(IServiceProvider provider) => provider.GetRequiredService<ISystemInfo>();

    /// <summary>
    /// on MongoDB a relay claims the messages it reads, and one it could not publish, or could not remove, is read again
    /// only when that claim expires: after a minute by default, which is longer than a scenario lasts.
    /// The other stores have no such claim, and take no notice.
    /// </summary>
    public static IServiceCollection ExpireOutboxClaimsAfter(this IServiceCollection services, TimeSpan claimDuration)
        => services.AddSingleton(new MongoOutboxRepositoryOptions(claimDuration));

    private sealed class FaultyPublisher(IPublisher inner, Fault fault, object host) : IPublisher
    {
        public ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            fault.ThrowIfOn(host);
            return inner.PublishAsync(envelope, cancellationToken);
        }
    }

    private sealed class FaultyOutbox(object host) : OutboxRepositoryInterceptor
    {
        public Fault? Reads { get; init; }
        public Fault? Deletes { get; init; }
        public Fault? Appends { get; init; }
        public Type? AppendedMessageType { get; init; }

        public override ValueTask<OutboxAppendResult> AppendAsync(IOutboxRepository repository, IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken)
        {
            // more than once: the messages of a saga are a queue, not a sequence that can only be read once
            if (Appends is not null && messages.Any(message => message.MessageType == AppendedMessageType))
                Appends.ThrowIfOn(host);

            return base.AppendAsync(repository, messages, cancellationToken);
        }

        public override ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(IOutboxRepository repository, CancellationToken cancellationToken)
        {
            Reads?.ThrowIfOn(host);
            return base.ReadPendingAsync(repository, cancellationToken);
        }

        public override ValueTask DeleteAsync(IOutboxRepository repository, MessageEnvelope message, CancellationToken cancellationToken)
        {
            Deletes?.ThrowIfOn(host);
            return base.DeleteAsync(repository, message, cancellationToken);
        }

        public override ValueTask DeleteAsync(IOutboxBatchRepository repository, IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken)
        {
            Deletes?.ThrowIfOn(host);
            return base.DeleteAsync(repository, messages, cancellationToken);
        }
    }

    private sealed class FaultyCommit(Fault fault, Type publishedMessageType, object host) : SagaStateRepositoryInterceptor
    {
        public override ValueTask CommitAsync(ISagaStateCommitter repository, ISagaInstance state, CancellationToken cancellationToken)
        {
            if (state.Outbox.Any(message => message.MessageType == publishedMessageType))
                fault.ThrowIfOn(host);

            return base.CommitAsync(repository, state, cancellationToken);
        }
    }
}
