using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.InMemory;

/// <summary>
/// what the saga pipeline uses to reach the in-memory saga store. It commits a saga in an order that leaves nothing
/// behind when the state cannot be stored: the state is serialized first, then the messages are appended to the outbox,
/// then the saga is stored and released.
/// </summary>
internal sealed class InMemorySagaStateCommitter : ISagaStateRepository, ISagaStateCommitter
{
    private readonly InMemorySagaStateRepository _repository;
    private readonly IOutboxRepository _outboxRepository;

    public InMemorySagaStateCommitter(InMemorySagaStateRepository repository, IOutboxRepository outboxRepository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
    }

    public ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
        => _repository.FindAsync(descriptor, messageContext, cancellationToken);

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => _repository.LockAsync(state, cancellationToken);

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => _repository.ReleaseAsync(state, cancellationToken);

    public ValueTask AbandonAsync(ISagaInstance state, CancellationToken cancellationToken = default)
        => _repository.AbandonAsync(state, cancellationToken);

    public async ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var stateData = _repository.SerializeState(state);

        if (state.Outbox.Count > 0)
            await _outboxRepository.AppendAsync(state.Outbox, cancellationToken)
                                   .ConfigureAwait(false);

        _repository.Release(state, stateData);
    }
}
