using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQL
{
    [ExcludeFromCodeCoverage]
    public record SqlConfiguration(string ConnectionString,
        SqlSagaStateRepositoryOptions SagaRepositoryOptions,
        SqlOutboxRepositoryOptions OutboxRepositoryOptions)
    {
        public SqlConfiguration(string connectionString) : this(connectionString, SqlSagaStateRepositoryOptions.Default, SqlOutboxRepositoryOptions.Default) { }
    }
}