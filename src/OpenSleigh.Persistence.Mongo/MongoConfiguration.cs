using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.Mongo
{
    [ExcludeFromCodeCoverage]
    public record MongoConfiguration(string ConnectionString, string DbName)
    {
        public MongoConfiguration(string connectionString, string dbName,
            MongoSagaStateRepositoryOptions sagaRepositoryOptions,
            MongoOutboxRepositoryOptions outboxRepositoryOptions) : this(connectionString, dbName)
        {
            this.SagaRepositoryOptions = sagaRepositoryOptions;
            this.OutboxRepositoryOptions = outboxRepositoryOptions;
        }

        public MongoSagaStateRepositoryOptions SagaRepositoryOptions { get; init; } =
            MongoSagaStateRepositoryOptions.Default;

        public MongoOutboxRepositoryOptions OutboxRepositoryOptions { get; init; } =
            MongoOutboxRepositoryOptions.Default;
    }
}