using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.SQLServer;

internal class MSSqlOutboxRepository : SqlOutboxRepository
{
    public MSSqlOutboxRepository(
        SagaDbContext dbContext, 
        ITypeResolver typeResolver, 
        SqlOutboxRepositoryOptions options, 
        ISerializer serializer, 
        DuplicateKeyDetector duplicateKeyDetector,
        Func<SagaDbContext>? relayDbContext = null) : base(options, dbContext, typeResolver, serializer, duplicateKeyDetector, relayDbContext)
    {
    }

    public override async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var entities = await RelayDbContext.OutboxMessages
           .AsNoTracking()
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

    // SQL Server is not given one statement listing the keys: depending on the version it reads the list as JSON,
    // which needs compatibility level 130, and it may scan the table for it, waiting on rows another relay holds.
    // The rows are deleted by key instead, one statement each, all sent together
    protected override async ValueTask DeleteBatchAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var dbContext = RelayDbContext;
        var rows = messages.DistinctBy(message => message.MessageId).Select(ToKeyOnlyRow).ToArray();

        try
        {
            dbContext.OutboxMessages.AttachRange(rows);
            dbContext.OutboxMessages.RemoveRange(rows);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // one of the rows was already gone: the others are deleted one by one, which does not mind
            StopTracking(dbContext, rows);
            foreach (var message in messages)
                await DeleteAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            StopTracking(dbContext, rows);
            throw;
        }
    }

    private static void StopTracking(SagaDbContext dbContext, IEnumerable<OutboxMessage> rows)
    {
        foreach (var row in rows)
            dbContext.Entry(row).State = EntityState.Detached;
    }

    private static OutboxMessage ToKeyOnlyRow(MessageEnvelope message)
        => new()
        {
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            SenderId = message.SenderId,
            MessageType = string.Empty,
            Body = [],
            CreatedAt = message.CreatedAt,
        };
}