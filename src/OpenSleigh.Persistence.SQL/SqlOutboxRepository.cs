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


public abstract class SqlOutboxRepository : IOutboxRepository
{
    protected readonly SqlOutboxRepositoryOptions _options;
    protected readonly ISerializer _serializer;
    protected readonly ITypeResolver _typeResolver;
    
    private readonly DuplicateKeyDetector _duplicateKeyDetector;

    private static SemaphoreSlim _appendSemaphore = new(1, 1);

    public SqlOutboxRepository(
        SqlOutboxRepositoryOptions options,
        SagaDbContext dbContext,
        ITypeResolver typeResolver,
        ISerializer serializer,
        DuplicateKeyDetector duplicateKeyDetector)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _duplicateKeyDetector = duplicateKeyDetector;
    }

    protected SagaDbContext DbContext { get; }

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!messages.Any())
            return ValueTask.FromResult(OutboxAppendResult.Success);

        return AppendAsyncCore(messages, cancellationToken);
    }

    private async ValueTask<OutboxAppendResult> AppendAsyncCore(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var entities = messages.Select(message => Entities.OutboxMessage.Map(message, _serializer));
        await _appendSemaphore.WaitAsync(cancellationToken);
       
        try
        {
            //TODO: this feels like a hack to make E2E tests work. Need to remove.
            //DbContext.ChangeTracker.Clear(); 
            
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
            _appendSemaphore.Release();
        }
    }

    public abstract ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default);

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return DeleteAsyncCore(message, cancellationToken);
    }

    private async ValueTask DeleteAsyncCore(MessageEnvelope message,  CancellationToken cancellationToken)
    {
        await DbContext.OutboxMessages.Where(e => e.MessageId == message.MessageId)
                                    .ExecuteDeleteAsync(cancellationToken)
                                    .ConfigureAwait(false);
    }
}
