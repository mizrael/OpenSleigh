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

    // what a returned message is stored with, in the order of the parameters of the command that stores it
    private static readonly (string Name, Func<Entities.OutboxMessage, object> Value)[] ReturnedMessageColumns =
    [
        (nameof(Entities.OutboxMessage.MessageId), row => row.MessageId),
        (nameof(Entities.OutboxMessage.CorrelationId), row => row.CorrelationId),
        (nameof(Entities.OutboxMessage.Body), row => row.Body),
        (nameof(Entities.OutboxMessage.MessageType), row => row.MessageType),
        (nameof(Entities.OutboxMessage.CreatedAt), row => row.CreatedAt),
        (nameof(Entities.OutboxMessage.SenderId), row => row.SenderId),
        (nameof(Entities.OutboxMessage.DeliverAt), row => row.DeliverAt!.Value),
        (nameof(Entities.OutboxMessage.FirstReturnedAt), row => row.FirstReturnedAt!.Value),
    ];

    /// <summary>the columns a returned message is stored with, not quoted, in the order of <see cref="ReturnedMessageParameters"/>.</summary>
    protected static IEnumerable<string> ReturnedMessageColumnNames => ReturnedMessageColumns.Select(column => column.Name);

    /// <summary>the placeholders of the values of message <paramref name="index"/>, from 0, one for each column.</summary>
    protected static IEnumerable<string> ReturnedMessageParameters(int index)
        => Enumerable.Range(index * ReturnedMessageColumns.Length, ReturnedMessageColumns.Length).Select(parameter => $"{{{parameter}}}");

    /// <summary>the placeholder of the value of a column of message <paramref name="index"/>, from 0.</summary>
    protected static string ReturnedMessageParameter(int index, string columnName)
        => $"{{{index * ReturnedMessageColumns.Length + Array.FindIndex(ReturnedMessageColumns, column => column.Name == columnName)}}}";

    // a command has a limited number of parameters on some databases
    private const int MaxMessagesPerPutBackCommand = 200;

    /// <summary>
    /// what a provider implements <see cref="IOutboxReturnRepository.PutBackAsync"/> with: stores the messages again
    /// with one command, as few round trips as a relay pass. A provider that does not implement the interface
    /// does not take returned messages back, which only a transport that returns messages asks for.
    /// </summary>
    /// <param name="buildCommand">
    /// builds the command for that many messages, each of them stored atomically: a row that exists gets the new due time,
    /// and the earlier of its time of first return and the new one; a row that does not is inserted. The command waits
    /// for a row a relay holds, finds rows by key, holds no lock on a row longer than it takes to store it, and does not
    /// fail when another host stores the same message at the same time. The messages come ordered by id.
    /// </param>
    protected async ValueTask StoreReturnedAsync(
        IReadOnlyCollection<MessageEnvelope> messages, Func<int, string> buildCommand, CancellationToken cancellationToken)
    {
        IOutboxReturnRepository.ThrowIfNotReturned(messages);
        if (messages.Count == 0)
            return;

        // always in the same order: two hosts that put the same messages back wait for each other
        // instead of each holding what the other needs
        var rows = messages
            .OrderBy(message => message.MessageId, StringComparer.Ordinal)
            .Select(message => Entities.OutboxMessage.Map(message, _serializer))
            .ToArray();

        await _appendSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var chunk in rows.Chunk(MaxMessagesPerPutBackCommand))
            {
                var parameters = chunk.SelectMany(row => ReturnedMessageColumns.Select(column => column.Value(row)));

                await DbContext.Database
                    .ExecuteSqlRawAsync(buildCommand(chunk.Length), parameters, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
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
