using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo
{
    public static class IMongoCollectionExtensions
    {
        public static async Task<T> FindOneAsync<T>(
            this IMongoCollection<T> collection, 
            FilterDefinition<T> filter,
            CancellationToken cancellationToken = default)
        {
            var cursor = await collection.FindAsync(filter, cancellationToken: cancellationToken)
                                         .ConfigureAwait(false);
            var entity = await cursor.FirstOrDefaultAsync(cancellationToken)
                                    .ConfigureAwait(false);
            return entity;
        }
    }
}