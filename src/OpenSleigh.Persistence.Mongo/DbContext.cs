using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo
{
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
            if (db == null)
                throw new ArgumentNullException(nameof(db));

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
        }

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
        }

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
            BsonDefaults.GuidRepresentationMode = GuidRepresentationMode.V3;

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
}