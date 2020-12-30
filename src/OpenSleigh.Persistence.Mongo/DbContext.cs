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
        private readonly IMongoDatabase _db;

        private static readonly IBsonSerializer<Guid> guidSerializer = new GuidSerializer(GuidRepresentation.Standard);
        private static readonly IBsonSerializer nullableGuidSerializer = new NullableSerializer<Guid>(guidSerializer);

        public DbContext(IMongoDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));

            SagaStates = _db.GetCollection<Entities.SagaState>("sagaStates");

            var indexBuilder = Builders<Entities.SagaState>.IndexKeys;
            var indexKeys = indexBuilder.Combine(
                indexBuilder.Ascending(e => e.CorrelationId),
                indexBuilder.Ascending(e => e.Type)
            );
            var index = new CreateIndexModel<SagaState>(indexKeys, new CreateIndexOptions()
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
                    mapper.AutoMap();

                    mapper.MapProperty(c => c.CorrelationId).SetSerializer(guidSerializer);
                    mapper.MapProperty(c => c.Type); 
                    mapper.MapProperty(c => c.Data);
                    mapper.MapProperty(c => c.LockId).SetSerializer(nullableGuidSerializer)
                                                     .SetDefaultValue(() => null);
                    mapper.MapProperty(c => c.LockTime).SetDefaultValue(() => null);
                    mapper.MapCreator(s => new Entities.SagaState(s._id, s.CorrelationId, s.Type, s.Data, s.LockId, s.LockTime));
                });
        }

        public IMongoCollection<Entities.SagaState> SagaStates { get; }
    }
}