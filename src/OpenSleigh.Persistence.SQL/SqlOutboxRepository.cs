using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Transactions;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public record SqlOutboxRepositoryOptions(TimeSpan LockMaxDuration, int MaxMessagesToPull)
{
    public static readonly SqlOutboxRepositoryOptions Default = new (TimeSpan.FromMinutes(1), 10);
}

public delegate bool DuplicateKeyDetector(Exception exception);


public class SqlOutboxRepository : IOutboxRepository
{
    private readonly SagaDbContext _dbContext;
    private readonly SqlOutboxRepositoryOptions _options;
    private readonly ITypeResolver _typeResolver;
    private readonly ISerializer _serializer;
    private readonly DuplicateKeyDetector _duplicateKeyDetector;

    public SqlOutboxRepository(
        SagaDbContext dbContext,
        ITypeResolver typeResolver,
        SqlOutboxRepositoryOptions options,
        ISerializer serializer,
        DuplicateKeyDetector duplicateKeyDetector)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _duplicateKeyDetector = duplicateKeyDetector;
    }

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return AppendAsyncCore(messages, cancellationToken);
    }

    private async ValueTask<OutboxAppendResult> AppendAsyncCore(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var entities = messages.Select(message => Entities.OutboxMessage.Map(message, _serializer));

        try
        {
            //TODO: this feels like a hack to make E2E tests work. Need to remove.
            //_dbContext.ChangeTracker.Clear(); 

            _dbContext.OutboxMessages.AddRange(entities);
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
            return OutboxAppendResult.Success;
        }
        catch (Exception ex) when (_duplicateKeyDetector(ex))
        {
            return OutboxAppendResult.Duplicate;
        }
    }

    public async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.OutboxMessages
           .Take(_options.MaxMessagesToPull)
           // make sure the QueryHintInterceptor is registered on the DbContext
           .WithHint(TableHints.UpdLock)
           .WithHint(TableHints.ReadPast)
           .ToListAsync(cancellationToken);

        var messages = new List<MessageEnvelope>(entities.Count);
        foreach (var entity in entities)
        {
            if (entity.TryMap(_typeResolver, _serializer, out var m))
                messages.Add(m);
        }
        return messages;
    }

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return DeleteAsyncCore(message, cancellationToken);
    }

    private async ValueTask DeleteAsyncCore(MessageEnvelope message,  CancellationToken cancellationToken)
    {
        var entity = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(e =>
                e.MessageId == message.MessageId,
                cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            throw new ArgumentException($"message '{message.MessageId}' not found");
       
        _dbContext.OutboxMessages.Remove(entity);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
