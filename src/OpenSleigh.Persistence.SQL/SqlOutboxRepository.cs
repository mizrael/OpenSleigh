using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public record SqlOutboxRepositoryOptions(TimeSpan LockMaxDuration, int MaxMessagesToPull)
{
    public static readonly SqlOutboxRepositoryOptions Default = new (TimeSpan.FromMinutes(1), 10);
}

public delegate bool DuplicateKeyDetector(Exception exception);


public abstract class SqlOutboxRepository : IOutboxRepository, IOutboxBatchRepository
{
    protected readonly SqlOutboxRepositoryOptions _options;
    protected readonly ISerializer _serializer;
    protected readonly ITypeResolver _typeResolver;
    
    private readonly DuplicateKeyDetector _duplicateKeyDetector;

    // a repository owns its DbContext, which cannot run two operations at once:
    // callers sharing one repository take turns, callers with their own repository do not wait for each other
    private readonly SemaphoreSlim _appendSemaphore = new(1, 1);

    private readonly Func<SagaDbContext>? _relayDbContext;

    /// <param name="relayDbContext">
    /// optional. Provides the context that pending messages are read from and deleted on. Passing the context
    /// the relay's transaction runs on is what keeps the rows it reads locked until that transaction ends,
    /// so that another relay skips them. When omitted, <paramref name="dbContext"/> is used.
    /// </param>
    public SqlOutboxRepository(
        SqlOutboxRepositoryOptions options,
        SagaDbContext dbContext,
        ITypeResolver typeResolver,
        ISerializer serializer,
        DuplicateKeyDetector duplicateKeyDetector,
        Func<SagaDbContext>? relayDbContext = null)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _duplicateKeyDetector = duplicateKeyDetector;
        _relayDbContext = relayDbContext;
    }

    protected SagaDbContext DbContext { get; }

    /// <summary>
    /// the context to read pending messages from and to delete them on.
    /// </summary>
    protected SagaDbContext RelayDbContext => _relayDbContext?.Invoke() ?? DbContext;

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!messages.Any())
            return ValueTask.FromResult(OutboxAppendResult.Success);

        return AppendAsyncCore(messages, cancellationToken);
    }

    private async ValueTask<OutboxAppendResult> AppendAsyncCore(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var entities = messages.Select(message => Entities.OutboxMessage.Map(message, _serializer)).ToArray();
        await _appendSemaphore.WaitAsync(cancellationToken);
       
        try
        {
            DbContext.OutboxMessages.AddRange(entities);
            await DbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
            return OutboxAppendResult.Success;
        }
        catch (Exception ex) when (_duplicateKeyDetector(ex))
        {
            return OutboxAppendResult.Duplicate;
        }
        finally
        {
            StopTracking(entities);
            _appendSemaphore.Release();
        }
    }

    // appended messages are never read back through this context. Left tracked, they would pile up
    // for as long as the repository lives, and a rejected duplicate would be re-sent with every later append
    private void StopTracking(IEnumerable<Entities.OutboxMessage> entities)
    {
        foreach (var entity in entities)
            DbContext.Entry(entity).State = EntityState.Detached;
    }

    /// <summary>
    /// the statement that reads, and locks for the relay's transaction, the messages to publish, skipping the rows
    /// another relay holds. Its parameters: {0} the current time, {1} how many messages to return at most,
    /// {2} how many of them, at most, can be delayed messages that are due.
    /// </summary>
    /// <remarks>
    /// Delayed messages that are due come first, up to their share: messages without a delay cannot keep them waiting,
    /// however many there are, and the other way round. Rows whose due time has not come must not be read at all.
    /// </remarks>
    protected abstract string ReadPendingQuery { get; }

    public virtual async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var batchSize = _options.MaxMessagesToPull;

        var entities = await RelayDbContext.OutboxMessages
            .FromSqlRaw(ReadPendingQuery, DateTimeOffset.UtcNow, batchSize, DelayedMessagesShare(batchSize))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var messages = new List<MessageEnvelope>(entities.Count);
        foreach (var entity in entities)
        {
            if (entity.TryMap(_typeResolver, _serializer, out var message))
                messages.Add(message);
        }
        return messages;
    }

    // half of a pass, rounded up
    private static int DelayedMessagesShare(int batchSize)
        => batchSize - batchSize / 2;

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return DeleteAsyncCore(message, cancellationToken);
    }

    private async ValueTask DeleteAsyncCore(MessageEnvelope message,  CancellationToken cancellationToken)
    {
        await RelayDbContext.OutboxMessages.Where(e => e.MessageId == message.MessageId)
                                    .ExecuteDeleteAsync(cancellationToken)
                                    .ConfigureAwait(false);
    }

    /// <summary>
    /// removes the messages of a relay pass with one round trip.
    /// </summary>
    public ValueTask DeleteAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            return ValueTask.CompletedTask;

        return DeleteBatchAsync(messages, cancellationToken);
    }

    /// <summary>
    /// removes the messages with one statement. A provider whose database would not look the rows up by key
    /// for such a statement overrides it.
    /// </summary>
    protected virtual async ValueTask DeleteBatchAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var messageIds = messages.Select(message => message.MessageId).ToArray();

        await RelayDbContext.OutboxMessages.Where(e => messageIds.Contains(e.MessageId))
                                    .ExecuteDeleteAsync(cancellationToken)
                                    .ConfigureAwait(false);
    }
}
