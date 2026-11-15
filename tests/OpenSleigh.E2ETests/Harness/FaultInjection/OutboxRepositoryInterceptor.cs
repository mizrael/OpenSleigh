using OpenSleigh.Outbox;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// sees every call to the outbox repository of a host and decides what happens: override a method to fail,
/// wait or count, and call the base to go on. What is not overridden goes straight to the repository.
/// Register it with <see cref="DecorationExtensions.Intercept(Microsoft.Extensions.DependencyInjection.IServiceCollection, Func{IServiceProvider, OutboxRepositoryInterceptor})"/>.
/// </summary>
public class OutboxRepositoryInterceptor
{
    public virtual ValueTask<OutboxAppendResult> AppendAsync(IOutboxRepository repository, IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken)
        => repository.AppendAsync(messages, cancellationToken);

    public virtual ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(IOutboxRepository repository, CancellationToken cancellationToken)
        => repository.ReadPendingAsync(cancellationToken);

    public virtual ValueTask DeleteAsync(IOutboxRepository repository, MessageEnvelope message, CancellationToken cancellationToken)
        => repository.DeleteAsync(message, cancellationToken);

    /// <summary>called only on a repository that removes a whole relay pass at once.</summary>
    public virtual ValueTask DeleteAsync(IOutboxBatchRepository repository, IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken)
        => repository.DeleteAsync(messages, cancellationToken);

    /// <summary>called only on a repository that takes back the messages a transport returned.</summary>
    public virtual ValueTask PutBackAsync(IOutboxReturnRepository repository, IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken)
        => repository.PutBackAsync(messages, cancellationToken);
}

internal class InterceptedOutboxRepository(IOutboxRepository inner, OutboxRepositoryInterceptor interceptor)
    : IOutboxRepository
{
    protected OutboxRepositoryInterceptor Interceptor { get; } = interceptor;

    // the library asks its repository whether it deletes in batches, and whether it takes returned messages back:
    // the replacement does exactly when the original does
    public static IOutboxRepository Create(IOutboxRepository inner, OutboxRepositoryInterceptor interceptor)
        => (inner as IOutboxBatchRepository, inner as IOutboxReturnRepository) switch
        {
            ({ } batch, { } returns) => new BatchingAndReturning(inner, batch, returns, interceptor),
            ({ } batch, null) => new Batching(inner, batch, interceptor),
            (null, { } returns) => new Returning(inner, returns, interceptor),
            _ => new InterceptedOutboxRepository(inner, interceptor),
        };

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
        => Interceptor.AppendAsync(inner, messages, cancellationToken);

    public ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
        => Interceptor.ReadPendingAsync(inner, cancellationToken);

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        => Interceptor.DeleteAsync(inner, message, cancellationToken);

    private class Batching(IOutboxRepository inner, IOutboxBatchRepository batch, OutboxRepositoryInterceptor interceptor)
        : InterceptedOutboxRepository(inner, interceptor), IOutboxBatchRepository
    {
        public ValueTask DeleteAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default)
            => Interceptor.DeleteAsync(batch, messages, cancellationToken);
    }

    private sealed class Returning(IOutboxRepository inner, IOutboxReturnRepository returns, OutboxRepositoryInterceptor interceptor)
        : InterceptedOutboxRepository(inner, interceptor), IOutboxReturnRepository
    {
        public ValueTask PutBackAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default)
            => Interceptor.PutBackAsync(returns, messages, cancellationToken);
    }

    private sealed class BatchingAndReturning(
        IOutboxRepository inner, IOutboxBatchRepository batch, IOutboxReturnRepository returns, OutboxRepositoryInterceptor interceptor)
        : Batching(inner, batch, interceptor), IOutboxReturnRepository
    {
        public ValueTask PutBackAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default)
            => Interceptor.PutBackAsync(returns, messages, cancellationToken);
    }
}
