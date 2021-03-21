using MongoDB.Driver;

namespace OpenSleigh.Persistence.Cosmos.Mongo
{
    public interface IDbContext
    {
        IMongoCollection<Entities.SagaState> SagaStates { get; }
        IMongoCollection<Entities.OutboxMessage> Outbox { get; }

        CosmosTransaction Transaction { get; set; }
    }
}