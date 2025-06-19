using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Utils;
using System.Runtime.CompilerServices;

namespace OpenSleigh.Persistence.PostgreSQL;

internal class PostgreOutboxRepository : SqlOutboxRepository
{
    private const string ReadQueryRaw = $"SELECT * FROM {Constants.DbSchema}.\"OutboxMessages\" FOR UPDATE SKIP LOCKED";
    private readonly static FormattableString ReadQuery = FormattableStringFactory.Create(ReadQueryRaw);

    public PostgreOutboxRepository(
        SqlOutboxRepositoryOptions options, 
        SagaDbContext dbContext, 
        ITypeResolver typeResolver, 
        ISerializer serializer, 
        DuplicateKeyDetector duplicateKeyDetector) : base(options, dbContext, typeResolver, serializer, duplicateKeyDetector)
    {
    }

    public override async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var entities = await DbContext.OutboxMessages
           .FromSql(ReadQuery)
           .Take(_options.MaxMessagesToPull)
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