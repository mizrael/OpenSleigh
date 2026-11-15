using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Persistence.Mongo;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the first deployment of a service: several instances start together, and none of them finds a database.
/// Each one creates what is missing, and all of them must end up started, on one database that works.
/// </summary>
public class ColdStartScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int HostsStartingTogether = 5;
    private const int Accounts = 5;

    // what is created at startup belongs to the store: the tables of a SQL database, the indexes of a MongoDB one.
    // The transport takes no part in it, so each store runs with one transport only, the one whose hosts start faster.
    // In memory there is nothing to create
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq],
        HostsCounts = [HostsStartingTogether])]
    public async Task hosts_that_start_together_on_an_empty_database_all_start(string combination, int hostsCount)
    {
        var accounts = Enumerable.Range(0, Accounts).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var runs = new HandlerRuns();
        var errors = new ConcurrentQueue<string>();

        var setup = new HostSetup(bus => bus.AddSaga<AccountSaga, AccountState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .AddLogging(logging => logging.AddProvider(new ErrorLog(errors)))
        };

        await RunScenarioAsync(combination,
            async (run, cancellationToken) =>
            {
                // a SQL host that cannot create its database does not start
                var hosts = await run.StartHostsAsync(hostsCount, setup);
                Assert.All(hosts, host => Assert.True(host.IsRunning));

                // and every one of them can use what they created
                await Task.WhenAll(accounts.Select((account, index)
                    => hosts[index % hosts.Count].PublishNewAsync(new OpenAccount(account), cancellationToken)));

                await run.WaitUntilAsync($"the {Accounts} accounts to be opened",
                    () => runs.HandledMessages == Accounts, cancellationToken);

                foreach (var account in accounts)
                    await run.WaitForReleasedSagaAsync<AccountSaga>(account, handledMessages: 1, cancellationToken);

                if (Combinations.Uses(combination, Combinations.MongoDb))
                    await AssertMongoIndexesAsync(hosts[0], cancellationToken);
            },
            prepareStore: false);

        // a MongoDB host creates its indexes with its first relay pass or its first message, whichever comes first,
        // and starts whatever becomes of them: one that failed to shows here, and nowhere else
        Assert.Empty(errors);
        Assert.Equal(Accounts, runs.Runs.Count);
    }

    private static async Task AssertMongoIndexesAsync(ScenarioHost host, CancellationToken cancellationToken)
    {
        var database = host.Services.GetRequiredService<IDbContext>();

        var sagaIndexes = await ReadIndexesAsync(database.SagaStates, cancellationToken);
        Assert.True(sagaIndexes["ix_correlation_type"]);
        Assert.False(sagaIndexes["ix_instance_id"]);

        var outboxIndexes = await ReadIndexesAsync(database.OutboxMessages, cancellationToken);
        Assert.True(outboxIndexes["ix_message_id"]);
        Assert.False(outboxIndexes["ix_deliver_at"]);
    }

    /// <returns>whether each index is unique, by name.</returns>
    private static async Task<IReadOnlyDictionary<string, bool>> ReadIndexesAsync<TDocument>(
        IMongoCollection<TDocument> collection, CancellationToken cancellationToken)
    {
        var indexes = await (await collection.Indexes.ListAsync(cancellationToken)).ToListAsync(cancellationToken);
        return indexes.ToDictionary(
            index => index["name"].AsString,
            index => index.GetValue("unique", BsonBoolean.False).ToBoolean());
    }

    /// <summary>keeps what the hosts log as an error.</summary>
    private sealed class ErrorLog(ConcurrentQueue<string> errors) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, errors);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    errors.Enqueue($"{category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
