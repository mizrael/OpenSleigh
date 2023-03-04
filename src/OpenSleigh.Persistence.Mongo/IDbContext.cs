using MongoDB.Driver;
using OpenSleigh.Persistence.Mongo.Entities;

namespace OpenSleigh.Persistence.Mongo
{
    public interface IDbContext
    {
        IMongoCollection<OutboxMessage> OutboxMessages { get; }
        IMongoCollection<SagaState> SagaStates { get; }
    }
}