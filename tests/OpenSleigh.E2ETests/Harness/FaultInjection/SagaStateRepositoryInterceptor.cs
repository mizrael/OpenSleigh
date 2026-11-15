using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// sees every call to the saga state repository of a host and decides what happens: override a method to fail,
/// wait or count, and call the base to go on. What is not overridden goes straight to the repository.
/// Register it with <see cref="DecorationExtensions.Intercept(Microsoft.Extensions.DependencyInjection.IServiceCollection, Func{IServiceProvider, SagaStateRepositoryInterceptor})"/>.
/// </summary>
public class SagaStateRepositoryInterceptor
{
    public virtual ValueTask<ISagaInstance?> FindAsync<TM>(
        ISagaStateRepository repository,
        SagaDescriptor descriptor,
        IMessageContext<TM> messageContext,
        CancellationToken cancellationToken)
        where TM : IMessage
        => repository.FindAsync(descriptor, messageContext, cancellationToken);

    public virtual ValueTask<string> LockAsync(ISagaStateRepository repository, ISagaInstance state, CancellationToken cancellationToken)
        => repository.LockAsync(state, cancellationToken);

    public virtual ValueTask ReleaseAsync(ISagaStateRepository repository, ISagaInstance state, CancellationToken cancellationToken)
        => repository.ReleaseAsync(state, cancellationToken);

    /// <summary>called when the saga is released without being stored: its handler failed, or was interrupted.</summary>
    public virtual ValueTask AbandonAsync(ISagaStateRepository repository, ISagaInstance state, CancellationToken cancellationToken)
        => repository.AbandonAsync(state, cancellationToken);

    /// <summary>called only on a repository that stores a saga and its messages together.</summary>
    public virtual ValueTask CommitAsync(ISagaStateCommitter repository, ISagaInstance state, CancellationToken cancellationToken)
        => repository.CommitAsync(state, cancellationToken);
}

internal class InterceptedSagaStateRepository(ISagaStateRepository inner, SagaStateRepositoryInterceptor interceptor)
    : ISagaStateRepository
{
    protected SagaStateRepositoryInterceptor Interceptor { get; } = interceptor;

    // the library asks whether its repository is a committer: the replacement is one exactly when the original is
    public static ISagaStateRepository Create(ISagaStateRepository inner, SagaStateRepositoryInterceptor interceptor)
        => inner is ISagaStateCommitter committer
            ? new Committing(inner, committer, interceptor)
            : new InterceptedSagaStateRepository(inner, interceptor);

    public ValueTask<ISagaInstance?> FindAsync<TM>(
        SagaDescriptor descriptor,
        IMessageContext<TM> messageContext,
        CancellationToken cancellationToken = default)
        where TM : IMessage
        => Interceptor.FindAsync(inner, descriptor, messageContext, cancellationToken);

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => Interceptor.LockAsync(inner, state, cancellationToken);

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => Interceptor.ReleaseAsync(inner, state, cancellationToken);

    public ValueTask AbandonAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => Interceptor.AbandonAsync(inner, state, cancellationToken);

    private sealed class Committing(ISagaStateRepository inner, ISagaStateCommitter committer, SagaStateRepositoryInterceptor interceptor)
        : InterceptedSagaStateRepository(inner, interceptor), ISagaStateCommitter
    {
        public ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default)
            => Interceptor.CommitAsync(committer, state, cancellationToken);
    }
}
