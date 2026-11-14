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

    public DbContext(IMongoDatabase db) : this(db, failureRecordsExpireAfter: null)
    {
    }

    /// <param name="failureRecordsExpireAfter">
    /// how long the records of failed messages are kept before the server removes them. Null to keep them
    /// until they are purged.
    /// </param>
    public DbContext(IMongoDatabase db, TimeSpan? failureRecordsExpireAfter)
    {
        ArgumentNullException.ThrowIfNull(db);

        OutboxMessages = db.GetCollection<Entities.OutboxMessage>("outbox");
        BuildOutboxIndexes();

        SagaStates = db.GetCollection<Entities.SagaState>("sagaStates");
        BuildSagaStatesIndexes();

        SagaMessageFailures = db.GetCollection<Entities.SagaFailureRecord>("sagaMessageFailures");
        BuildSagaMessageFailuresIndexes(failureRecordsExpireAfter);
    }

    internal const string FailuresOccurredAtIndexName = "ix_occurred_at";

    private void BuildSagaMessageFailuresIndexes(TimeSpan? expireAfter)
    {
        var indexBuilder = Builders<Entities.SagaFailureRecord>.IndexKeys;

        SagaMessageFailures.Indexes.CreateOne(new CreateIndexModel<Entities.SagaFailureRecord>(
            indexBuilder.Combine(indexBuilder.Ascending(e => e.InstanceId), indexBuilder.Ascending(e => e.MessageId)),
            new CreateIndexOptions { Name = "ix_instance_message" }));

        SagaMessageFailures.Indexes.CreateOne(new CreateIndexModel<Entities.SagaFailureRecord>(
            indexBuilder.Ascending(e => e.CorrelationId),
            new CreateIndexOptions { Name = "ix_correlation_id" }));

        // sorts and purges the records, and expires them when asked to
        var occurredAtIndex = new CreateIndexModel<Entities.SagaFailureRecord>(
            indexBuilder.Ascending(e => e.OccurredAt),
            new CreateIndexOptions { Name = FailuresOccurredAtIndexName, ExpireAfter = expireAfter });
        try
        {
            SagaMessageFailures.Indexes.CreateOne(occurredAtIndex);
        }
        catch (MongoCommandException ex) when (ex.Code is IndexOptionsConflictCode or IndexKeySpecsConflictCode)
        {
            // the index exists with another expiry, or none: an index cannot be created again with other options
            ReplaceIndex(SagaMessageFailures, occurredAtIndex, FailuresOccurredAtIndexName);
        }
    }

    // instances that start together with a new setting all get here: whoever comes second finds the index
    // already dropped, or already created again, by the first
    private static void ReplaceIndex<T>(IMongoCollection<T> collection, CreateIndexModel<T> index, string indexName)
    {
        try
        {
            collection.Indexes.DropOne(indexName);
        }
        catch (MongoCommandException ex) when (ex.Code == IndexNotFoundCode)
        {
        }

        try
        {
            collection.Indexes.CreateOne(index);
        }
        catch (MongoCommandException ex) when (ex.Code is IndexOptionsConflictCode or IndexKeySpecsConflictCode)
        {
        }
    }

    private const int IndexNotFoundCode = 27;

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

    public IMongoCollection<Entities.SagaFailureRecord> SagaMessageFailures { get; }
}