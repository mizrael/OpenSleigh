using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo
{
    public interface IDbContext
    {
        IMongoCollection<Entities.SagaState> SagaStates { get; }
        IMongoCollection<Entities.OutboxMessage> Outbox { get; }

        MongoTransaction Transaction { get; set; }
    }
}