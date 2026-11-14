using MongoDB.Driver;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.Mongo.Entities;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace OpenSleigh.Persistence.Mongo;

[ExcludeFromCodeCoverage]
public record MongoSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
{
    public static readonly MongoSagaStateRepositoryOptions Default = new MongoSagaStateRepositoryOptions(TimeSpan.FromMinutes(1));
}

// SPIKE, not for merging: the ledger experiment behind the existing repository interfaces.
// A saga is a sequence of documents, one per append, each holding the state after it. Finding a saga reads its
// latest document and asks whether the message was handled already, both at once. Storing it is one insert,
// refused by the unique index when someone else stored that version first: state, handled message and published
// messages are stored together, with no transaction. In lease mode a lease is written on the latest document
// before the handler, as the lock is today.
public class MongoSagaStateRepository : ISagaStateRepository, ISagaStateCommitter
{
    private const string NoLockId = "nolock";

    private sealed class Known
    {
        public int Version;
        public bool Stored;
        public string? ProcessedMessageId;
        public string? LeaseId;
    }

    private static readonly ConcurrentDictionary<Type, ILedgerInstanceFactory> _instanceFactories = new();
    private static readonly ConcurrentDictionary<Type, ISagaStateExtractor> _stateExtractors = new();

    private static readonly ProjectionDefinition<SagaLedgerEntry> WithoutLists =
        Builders<SagaLedgerEntry>.Projection.Exclude(e => e.Outbox).Exclude(e => e.MessageIds);

    private readonly IDbContext _dbContext;
    private readonly MongoSagaStateRepositoryOptions _options;
    private readonly ISerializer _serializer;
    private readonly IOutboxRepository? _outboxRepository;

    private readonly ConditionalWeakTable<ISagaInstance, Known> _known = new();

    public MongoSagaStateRepository(
        IDbContext dbContext,
        MongoSagaStateRepositoryOptions? options,
        ISerializer serializer,
        IOutboxRepository? outboxRepository = null)
    {
        _dbContext = dbContext;
        _options = options ?? MongoSagaStateRepositoryOptions.Default;
        _serializer = serializer;
        _outboxRepository = outboxRepository;
    }

    private static FilterDefinition<SagaLedgerEntry> OfSaga(string correlationId, SagaDescriptor descriptor)
    {
        var filter = Builders<SagaLedgerEntry>.Filter;
        return filter.And(
            filter.Eq(e => e.CorrelationId, correlationId),
            filter.Eq(e => e.SagaType, descriptor.SagaType.FullName),
            filter.Eq(e => e.SagaStateType, descriptor.SagaStateType?.FullName));
    }

    public async ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        var saga = OfSaga(messageContext.CorrelationId, descriptor);
        var handled = Builders<SagaLedgerEntry>.Filter.And(
            saga,
            Builders<SagaLedgerEntry>.Filter.AnyEq(e => e.MessageIds, messageContext.MessageId));

        // sent together: the two answers come back in the time of one
        var latestTask = _dbContext.SagaLedger
            .Find(saga)
            .SortByDescending(e => e.Version)
            .Limit(1)
            .Project<SagaLedgerEntry>(WithoutLists)
            .FirstOrDefaultAsync(cancellationToken);
        var processedTask = _dbContext.SagaLedger
            .Find(handled)
            .Limit(1)
            .AnyAsync(cancellationToken);

        await Task.WhenAll(latestTask, processedTask).ConfigureAwait(false);

        var latest = latestTask.Result;
        if (latest is null)
            return null;

        var processed = processedTask.Result;
        ProcessedMessage[] processedMessages = processed
            ? [new ProcessedMessage { MessageId = messageContext.MessageId, When = DateTimeOffset.UtcNow }]
            : [];

        ISagaInstance result;
        if (descriptor.SagaStateType is null)
            result = new SagaInstance(latest.InstanceId, latest.TriggerMessageId, latest.CorrelationId, descriptor, processedMessages);
        else
        {
            var state = latest.StateData is null
                ? Activator.CreateInstance(descriptor.SagaStateType)
                : _serializer.Deserialize(latest.StateData, descriptor.SagaStateType);
            var factory = _instanceFactories.GetOrAdd(descriptor.SagaStateType, static t =>
                (ILedgerInstanceFactory)Activator.CreateInstance(typeof(LedgerInstanceFactory<>).MakeGenericType(t))!);
            result = factory.Create(state!, latest.InstanceId, latest.TriggerMessageId, latest.CorrelationId, descriptor, processedMessages);
        }

        if (latest.IsCompleted)
            result.MarkAsCompleted();

        _known.AddOrUpdate(result, new Known
        {
            Version = latest.Version,
            Stored = true,
            ProcessedMessageId = processed ? messageContext.MessageId : null
        });

        return result;
    }

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (LedgerSpike.Mode == LedgerConcurrencyMode.NoLock)
        {
            if (!_known.TryGetValue(state, out _))
                _known.AddOrUpdate(state, new Known());
            return ValueTask.FromResult(NoLockId);
        }

        return TakeLeaseAsync(state, cancellationToken);
    }

    private async ValueTask<string> TakeLeaseAsync(ISagaInstance state, CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var filter = Builders<SagaLedgerEntry>.Filter;

        if (_known.TryGetValue(state, out var known) && known.Stored)
        {
            var free = filter.And(
                filter.Eq(e => e.InstanceId, state.InstanceId),
                filter.Eq(e => e.Version, known.Version),
                filter.Or(
                    filter.Eq(e => e.LeaseTime, null),
                    filter.Lt(e => e.LeaseTime, now - _options.LockMaxDuration)));
            var take = Builders<SagaLedgerEntry>.Update
                .Set(e => e.LeaseId, leaseId)
                .Set(e => e.LeaseTime, now);

            var taken = await _dbContext.SagaLedger.UpdateOneAsync(free, take, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            if (!taken.IsAcknowledged || taken.MatchedCount > 0)
            {
                known.LeaseId = leaseId;
                return leaseId;
            }

            // the saga is leased by someone else, or no longer as it was found: its version tells which
            var latest = await _dbContext.SagaLedger
                .Find(filter.Eq(e => e.InstanceId, state.InstanceId))
                .SortByDescending(e => e.Version)
                .Limit(1)
                .Project(e => e.Version)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (latest.Count == 0 || latest[0] != known.Version)
            {
                _known.Remove(state);
                throw new OptimisticLockException($"saga state '{state.InstanceId}' has changed since it was found");
            }

            throw new LockException($"saga state '{state.InstanceId}' is already locked");
        }

        // a new saga is stored with its lease, so that whoever finds it before its first message is done with waits
        var first = NewEntry(state, version: 0, isStart: true, messageIds: [], ExtractStateData(state));
        first.LeaseId = leaseId;
        first.LeaseTime = now;

        if (!await TryInsertAsync(first).ConfigureAwait(false))
            throw new OptimisticLockException($"saga state for correlation '{state.CorrelationId}' was created by someone else");

        _known.AddOrUpdate(state, new Known { Version = 0, Stored = true, LeaseId = leaseId });
        return leaseId;
    }

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return AppendAsync(state, Array.Empty<MessageEnvelope>(), cancellationToken);
    }

    public ValueTask CommitAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return AppendAsync(state, state.Outbox, cancellationToken);
    }

    private async ValueTask AppendAsync(ISagaInstance state, IReadOnlyCollection<MessageEnvelope> outbox, CancellationToken cancellationToken)
    {
        if (!_known.TryGetValue(state, out var known))
            known = new Known();

        var messageIds = state.ProcessedMessages
            .Where(message => message.MessageId != known.ProcessedMessageId)
            .Select(message => message.MessageId)
            .ToArray();

        // nothing was handled: nothing of the saga is stored, and its lease, if it has one, is given back
        if (messageIds.Length == 0)
        {
            await GiveLeaseBackAsync(state, known).ConfigureAwait(false);
            _known.Remove(state);
            return;
        }

        // messages with a due time go through the outbox collection, after the saga is stored: the relay of this
        // experiment only reads, from the documents of a saga, messages that are all due at once
        var embedded = outbox.Count > 0 && outbox.All(message => message.DeliverAt is null);

        var entry = NewEntry(
            state,
            version: known.Version + 1,
            isStart: !known.Stored,
            messageIds,
            ExtractStateData(state),
            embedded ? outbox.Select(message => OutboxMessage.Create(message, _serializer)).ToList() : null);

        var stored = await TryInsertAsync(entry).ConfigureAwait(false);
        _known.Remove(state);

        if (!stored)
        {
            LedgerSpike.CountAppendConflict();

            if (known.LeaseId is null)
                throw new OptimisticLockException($"saga state '{state.InstanceId}' has changed since it was found");

            throw new LockException($"unable to release Saga State '{state.InstanceId}' with lock id '{state.LockId}'");
        }

        if (!embedded && outbox.Count > 0 && _outboxRepository is not null)
            await _outboxRepository.AppendAsync(outbox, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask GiveLeaseBackAsync(ISagaInstance state, Known known)
    {
        if (known.LeaseId is null)
            return;

        var filter = Builders<SagaLedgerEntry>.Filter;
        var held = filter.And(
            filter.Eq(e => e.InstanceId, state.InstanceId),
            filter.Eq(e => e.Version, known.Version),
            filter.Eq(e => e.LeaseId, known.LeaseId));
        var giveBack = Builders<SagaLedgerEntry>.Update
            .Set(e => e.LeaseId, null)
            .Set(e => e.LeaseTime, null);

        await _dbContext.SagaLedger.UpdateOneAsync(held, giveBack, cancellationToken: CancellationToken.None).ConfigureAwait(false);
    }

    private static SagaLedgerEntry NewEntry(
        ISagaInstance state, int version, bool isStart, string[] messageIds, byte[]? stateData, List<OutboxMessage>? outbox = null)
        => new()
        {
            InstanceId = state.InstanceId,
            Version = version,
            CorrelationId = state.CorrelationId,
            SagaType = state.Descriptor.SagaType.FullName!,
            SagaStateType = state.Descriptor.SagaStateType?.FullName,
            TriggerMessageId = state.TriggerMessageId,
            IsStart = isStart ? true : null,
            MessageIds = messageIds,
            OccurredAt = DateTime.UtcNow,
            IsCompleted = state.IsCompleted,
            StateData = stateData,
            Outbox = outbox,
            Pending = outbox is null ? null : true,
        };

    // one insert: refused when someone else stored this version of the saga, started the saga, or handled the message first.
    // Not cancellable: cancelled in flight it can still be applied
    private async ValueTask<bool> TryInsertAsync(SagaLedgerEntry entry)
    {
        try
        {
            await _dbContext.SagaLedger.InsertOneAsync(entry, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private byte[]? ExtractStateData(ISagaInstance state)
    {
        if (state.Descriptor.SagaStateType is null || !state.GetType().IsGenericType)
            return null;

        var extractor = _stateExtractors.GetOrAdd(state.Descriptor.SagaStateType, static t =>
            (ISagaStateExtractor)Activator.CreateInstance(
                typeof(SagaStateExtractor<>).MakeGenericType(t))!);
        return extractor.Extract(state, _serializer);
    }
}

internal interface ILedgerInstanceFactory
{
    ISagaInstance Create(
        object state, string instanceId, string triggerMessageId, string correlationId,
        SagaDescriptor descriptor, IEnumerable<ProcessedMessage> processedMessages);
}

internal sealed class LedgerInstanceFactory<TS> : ILedgerInstanceFactory
{
    public ISagaInstance Create(
        object state, string instanceId, string triggerMessageId, string correlationId,
        SagaDescriptor descriptor, IEnumerable<ProcessedMessage> processedMessages)
        => new SagaInstance<TS>(instanceId, triggerMessageId, correlationId, descriptor, (TS)state, processedMessages);
}
