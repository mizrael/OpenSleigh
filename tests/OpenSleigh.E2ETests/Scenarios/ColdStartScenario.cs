using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
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
    // In memory there is nothing to create. The SQL cases are excluded by the known defect 'cold-start-schema-race'
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq],
        HostsCounts = [HostsStartingTogether])]
    public async Task hosts_that_start_together_on_an_empty_database_all_start(string combination, int hostsCount)
    {
        var accounts = Enumerable.Range(0, Accounts).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var runs = new HandlerRuns(Accounts);

        var setup = new HostSetup(bus => bus.AddSaga<AccountSaga, AccountState>())
        {
            ConfigureServices = services => services.AddSingleton(runs)
        };

        await RunScenarioAsync(combination,
            async (run, cancellationToken) =>
            {
                var hosts = await run.StartHostsAsync(hostsCount, setup);
                Assert.All(hosts, host => Assert.True(host.IsRunning));

                // and every one of them can use what they created
                await Task.WhenAll(accounts.Select((account, index)
                    => hosts[index % hosts.Count].PublishAsync(new OpenAccount(account), cancellationToken)));

                await runs.AllHandled.WaitAsync(cancellationToken);

                foreach (var account in accounts)
                    await run.WaitForSagaAsync<AccountSaga>(account,
                        saga => saga.ProcessedMessages.Count == 1 && !saga.IsLocked, cancellationToken);
            },
            prepareStore: false);

        Assert.Equal(Accounts, runs.Runs.Count);
    }
}
