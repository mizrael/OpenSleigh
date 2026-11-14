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
    }

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