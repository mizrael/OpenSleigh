using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
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
}