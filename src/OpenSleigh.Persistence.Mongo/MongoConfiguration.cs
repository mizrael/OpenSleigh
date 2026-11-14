using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.Mongo;

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

    /// <summary>
    /// how long the records of the messages whose handler failed are kept before the server removes them,
    /// with an expiry index. Null, the default, keeps them until they are purged (<c>ISagaFailurePurger</c>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">the value is not positive.</exception>
    public TimeSpan? FailureRecordsExpireAfter
    {
        get => _failureRecordsExpireAfter;
        init
        {
            if (value is { } expiry)
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiry, TimeSpan.Zero);
            _failureRecordsExpireAfter = value;
        }
    }

    private readonly TimeSpan? _failureRecordsExpireAfter;
}