using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using System;
using OpenSleigh.Persistence.Mongo.Entities;

namespace OpenSleigh.Persistence.Mongo
{
    public class DbContext : IDbContext
    {
        private static readonly IBsonSerializer<Guid> guidSerializer = new GuidSerializer(GuidRepresentation.Standard);
        private static readonly IBsonSerializer nullableGuidSerializer = new NullableSerializer<Guid>(guidSerializer);

        public DbContext(IMongoDatabase db)
        {
            if (db == null) 
                throw new ArgumentNullException(nameof(db));
            
            Outbox = db.GetCollection<Entities.OutboxMessage>("outbox");
            BuildOutboxIndexes();
            
            SagaStates = db.GetCollection<Entities.SagaState>("sagaStates");
            BuildSagaStatesIndexes();
        }

        private void BuildOutboxIndexes()
        {
            var indexBuilder = Builders<Entities.OutboxMessage>.IndexKeys;
            var indexKeys = indexBuilder.Ascending(e => e.Status);
            
            var index = new CreateIndexModel<OutboxMessage>(indexKeys, new CreateIndexOptions()
            {
                Unique = false,
                Name = "ix_status"
            });
            Outbox.Indexes.CreateOne(index);
        }

        private void BuildSagaStatesIndexes()
        {
            var indexBuilder = Builders<Entities.SagaState>.IndexKeys;
            var indexKeys = indexBuilder.Combine(
                indexBuilder.Ascending(e => e.CorrelationId),
                indexBuilder.Ascending(e => e.Type)
            );
            var index = new CreateIndexModel<Entities.SagaState>(indexKeys, new CreateIndexOptions()
            {
                Unique = true,
                Name = "ix_correlation_type"
            });
            SagaStates.Indexes.CreateOne(index);
        }

        static DbContext()
        {
            BsonDefaults.GuidRepresentationMode = GuidRepresentationMode.V3;

            if (!BsonClassMap.IsClassMapRegistered(typeof(Entities.SagaState)))
                BsonClassMap.RegisterClassMap<Entities.SagaState>(mapper =>
                {
                    mapper.MapIdProperty(c => c._id);
                    mapper.MapProperty(c => c.CorrelationId).SetSerializer(guidSerializer);
                    mapper.MapProperty(c => c.Type); 
                    mapper.MapProperty(c => c.Data);
                    mapper.MapProperty(c => c.LockId).SetSerializer(nullableGuidSerializer)
                                                     .SetDefaultValue(() => null);
                    mapper.MapProperty(c => c.LockTime).SetDefaultValue(() => null);
                    mapper.MapCreator(s => new Entities.SagaState(s._id, s.CorrelationId, s.Type, s.Data, s.LockId, s.LockTime));
                });

            if (!BsonClassMap.IsClassMapRegistered(typeof(Entities.OutboxMessage)))
                BsonClassMap.RegisterClassMap<Entities.OutboxMessage>(mapper =>
                {
                    mapper.MapIdProperty(c => c.Id).SetSerializer(guidSerializer);
                    mapper.MapProperty(c => c.Data);
                    mapper.MapProperty(c => c.Type);
                    mapper.MapProperty(c => c.Status);
                    mapper.MapProperty(c => c.PublishingDate).SetDefaultValue(() => null);
                    mapper.MapCreator(s => new Entities.OutboxMessage(s.Id, s.Data, s.Type, s.Status, s.PublishingDate));
                });
        }

        public IMongoCollection<Entities.SagaState> SagaStates { get; }

        public IMongoCollection<Entities.OutboxMessage> Outbox { get; }
    }
}