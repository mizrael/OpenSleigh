using MongoDB.Driver;

namespace OpenSleigh.Persistence.Cosmos
{
    public interface IDbContext
    {
        IMongoCollection<Entities.SagaState> SagaStates { get; }
        IMongoCollection<Entities.OutboxMessage> Outbox { get; }

        CosmosTransaction Transaction { get; set; }
    }
}