using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo;

public class DbContext : IDbContext
{
    private static readonly IBsonSerializer<Guid> guidSerializer = new GuidSerializer(GuidRepresentation.Standard);
    private static readonly IBsonSerializer nullableGuidSerializer = new NullableSerializer<Guid>(guidSerializer);

    static DbContext()
    {
        ConfigureMappings();
    }

    public DbContext(IMongoDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);

        OutboxMessages = db.GetCollection<Entities.OutboxMessage>("outbox");
        BuildOutboxIndexes();

        SagaStates = db.GetCollection<Entities.SagaState>("sagaStates");
        BuildSagaStatesIndexes();

        SagaLedger = db.GetCollection<Entities.SagaLedgerEntry>("sagaLedger");
        BuildSagaLedgerIndexes();
    }

    // SPIKE: the indexes of the ledger experiment
    private void BuildSagaLedgerIndexes()
    {
        var keys = Builders<Entities.SagaLedgerEntry>.IndexKeys;
        var filter = Builders<Entities.SagaLedgerEntry>.Filter;

        SagaLedger.Indexes.CreateMany(
        [
            // the append: a second document for the same version is refused
            new CreateIndexModel<Entities.SagaLedgerEntry>(
                keys.Ascending(e => e.InstanceId).Ascending(e => e.Version),
                new CreateIndexOptions { Unique = true, Name = "ix_instance_version" }),

            // the latest document of a saga
            new CreateIndexModel<Entities.SagaLedgerEntry>(
                keys.Ascending(e => e.CorrelationId).Ascending(e => e.SagaType).Ascending(e => e.SagaStateType).Descending(e => e.Version),
                new CreateIndexOptions { Name = "ix_saga_latest" }),

            // one saga per correlation id
            new CreateIndexModel<Entities.SagaLedgerEntry>(
                keys.Ascending(e => e.CorrelationId).Ascending(e => e.SagaType).Ascending(e => e.SagaStateType),
                new CreateIndexOptions<Entities.SagaLedgerEntry>
                {
                    Unique = true,
                    Name = "ix_saga_start",
                    PartialFilterExpression = filter.Eq(e => e.IsStart, true)
                }),

            // idempotency: a message is handled once by a saga
            new CreateIndexModel<Entities.SagaLedgerEntry>(
                keys.Ascending(e => e.CorrelationId).Ascending(e => e.SagaType).Ascending(e => e.SagaStateType).Ascending(e => e.MessageIds),
                new CreateIndexOptions { Unique = true, Name = "ix_saga_message" }),

            // the relay looks up the documents that have messages to send
            new CreateIndexModel<Entities.SagaLedgerEntry>(
                keys.Ascending(e => e.Pending).Ascending(e => e.ClaimTime),
                new CreateIndexOptions<Entities.SagaLedgerEntry>
                {
                    Name = "ix_pending",
                    PartialFilterExpression = filter.Eq(e => e.Pending, true)
                }),
        ]);
    }

    public IMongoCollection<Entities.SagaLedgerEntry> SagaLedger { get; }

    private void BuildOutboxIndexes()
    {
        var indexBuilder = Builders<Entities.OutboxMessage>.IndexKeys;
        var indexKeys = indexBuilder.Ascending(e => e.MessageId);

        var index = new CreateIndexModel<Entities.OutboxMessage>(indexKeys, new CreateIndexOptions()
        {
            Unique = true,
            Name = "ix_message_id"
        });
        OutboxMessages.Indexes.CreateOne(index);

        // the relay looks up the messages that are due. Not sparse: a message without a due time is due
        var deliverAtIndex = new CreateIndexModel<Entities.OutboxMessage>(
            indexBuilder.Ascending(e => e.DeliverAt),
            new CreateIndexOptions()
            {
                Name = OutboxDeliverAtIndexName
            });
        try
        {
            OutboxMessages.Indexes.CreateOne(deliverAtIndex);
        }
        catch (MongoCommandException ex) when (ex.Code is IndexOptionsConflictCode or IndexKeySpecsConflictCode)
        {
            // the collection already has an index on this key, created by someone else under another name
            // or with other options: failing here would stop the application from starting
        }
    }

    internal const string OutboxDeliverAtIndexName = "ix_deliver_at";

    private void BuildSagaStatesIndexes()
    {
        var indexBuilder = Builders<Entities.SagaState>.IndexKeys;
        var indexKeys = indexBuilder.Combine(
            indexBuilder.Ascending(e => e.CorrelationId),
            indexBuilder.Ascending(e => e.SagaType),
            indexBuilder.Ascending(e => e.SagaStateType)
        );
        var index = new CreateIndexModel<Entities.SagaState>(indexKeys, new CreateIndexOptions()
        {
            Unique = true,
            Name = "ix_correlation_type"
        });
        SagaStates.Indexes.CreateOne(index);

        // lock and release look a saga up by instance id
        var instanceIdIndex = new CreateIndexModel<Entities.SagaState>(
            indexBuilder.Ascending(e => e.InstanceId),
            new CreateIndexOptions()
            {
                Name = "ix_instance_id"
            });
        try
        {
            SagaStates.Indexes.CreateOne(instanceIdIndex);
        }
        catch (MongoCommandException ex) when (ex.Code is IndexOptionsConflictCode or IndexKeySpecsConflictCode)
        {
            // the collection already has an index on this key, created by someone else under another name
            // or with other options. It serves the same lookups: failing here would stop the application from starting
        }
    }

    private const int IndexOptionsConflictCode = 85;
    private const int IndexKeySpecsConflictCode = 86;

    private static void TryRegisterClassMap<T>(Action<BsonClassMap<T>> mapAction)
    {
        try
        {
            BsonClassMap.RegisterClassMap(mapAction);
        }
        catch
        {
            // swallowing exception, in case another concurrent thread has already registered the mapping
        }
    }

    private static void ConfigureMappings()
    {
        //TODO: not sure this one is necessary
        TryRegisterClassMap<Entities.SagaProcessedMessage>(mapper =>
        {
            mapper.AutoMap();
        });

        TryRegisterClassMap<Entities.SagaState>(mapper =>
        {
            mapper.MapIdProperty(c => c.Id);
            mapper.MapProperty(c => c.CorrelationId).SetSerializer(guidSerializer);
            mapper.MapProperty(c => c.InstanceId).SetSerializer(guidSerializer);
            mapper.MapProperty(c => c.TriggerMessageId).SetSerializer(guidSerializer);

            mapper.MapProperty(c => c.SagaType);
            mapper.MapProperty(c => c.SagaStateType);
            mapper.MapProperty(c => c.StateData);

            mapper.MapProperty(c => c.IsCompleted);

            mapper.MapProperty(c => c.ProcessedMessages);

            mapper.MapProperty(c => c.LockId)
                .SetSerializer(nullableGuidSerializer)
                .SetDefaultValue(() => null);
            mapper.MapProperty(c => c.LockTime)
                .SetDefaultValue(() => null);
        });

        TryRegisterClassMap<Entities.OutboxMessage>(mapper =>
        {
            mapper.MapIdProperty(c => c.Id);

            mapper.MapProperty(c => c.ParentId).SetSerializer(guidSerializer);
            mapper.MapProperty(c => c.SenderId).SetSerializer(guidSerializer);
            mapper.MapProperty(c => c.MessageId).SetSerializer(guidSerializer);
            mapper.MapProperty(c => c.MessageType);
            mapper.MapProperty(c => c.Body);
            mapper.MapProperty(c => c.CorrelationId).SetSerializer(guidSerializer);
            mapper.MapProperty(c => c.CreatedAt).SetDefaultValue(() => null);
            
            mapper.MapProperty(c => c.LockId).SetSerializer(nullableGuidSerializer)
                .SetDefaultValue(() => null);
            mapper.MapProperty(c => c.LockTime).SetDefaultValue(() => null);                
        });
    }

    public IMongoCollection<Entities.SagaState> SagaStates { get; }

    public IMongoCollection<Entities.OutboxMessage> OutboxMessages { get; }
}