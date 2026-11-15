using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// many messages for the same saga, published at the same time from every host: whoever gets one finds the saga
/// free or locked by another. A locked saga is a retry, never a loss and never a second run.
/// </summary>
public class ConcurrentMessagesScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int OtherAccounts = 2;
    private const int MovementsPerAccount = 12;
    private const int MessagesPerAccount = 1 + MovementsPerAccount;
    private const int Messages = (1 + OtherAccounts) * MessagesPerAccount;

    // the hosts that compete for the messages of one saga: one host of these transports handles its messages
    // one at a time. The lock is the store's and the retry is the transport's, so every store runs with one
    // transport and one store with the other: the remaining pairs would show the same lock and the same retry again
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [5])]
    public Task every_message_for_one_saga_is_handled_exactly_once_by_hosts_that_compete(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    // in memory a host handles the messages it reads together at the same time: it competes with itself
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task every_message_for_one_saga_is_handled_exactly_once_by_a_host_that_handles_several_at_once(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    private async Task RunAsync(string combination, int hostsCount)
    {
        var contended = Guid.NewGuid().ToString();
        var others = Enumerable.Range(0, OtherAccounts).Select(_ => Guid.NewGuid().ToString()).ToArray();
        string[] accounts = [contended, .. others];

        var runs = new HandlerRuns();
        var batch = new InMemoryBatch();

        var setup = new HostSetup(bus => bus.AddSaga<AccountSaga, AccountState>())
        {
            ConfigureServices = services => services.RecordHandlerRuns(runs).ReadInMemoryMessagesIn(batch)
        };

        var stored = new List<SagaInstanceInfo>();

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                // a movement that reached a host before its account is stored would be dead-lettered, by design:
                // the accounts are opened first, so that no movement can be early and every dead letter is a failure
                foreach (var account in accounts)
                    await run.AnyHost.PublishNewAsync(new OpenAccount(account), cancellationToken);
                foreach (var account in accounts)
                    await run.WaitForReleasedSagaAsync<AccountSaga>(account, handledMessages: 1, cancellationToken);

                // the movements of one account are made to meet. Those of the others meet if they happen to
                using (batch.Of(InMemoryBatch.MaxSize))
                    await run.ForceLockConflictAsync(runs,
                        handlerRun => handlerRun.CorrelationId == contended,
                        () => PublishFromEveryHostAsync(run, MovementsOf(contended), cancellationToken),
                        cancellationToken);

                await PublishFromEveryHostAsync(run, others.SelectMany(MovementsOf), cancellationToken);

                await run.WaitUntilAsync($"the {Messages} messages to be handled",
                    () => runs.HandledMessages == Messages, cancellationToken);

                foreach (var account in accounts)
                    stored.Add(await run.WaitForReleasedSagaAsync<AccountSaga>(account, MessagesPerAccount, cancellationToken));
            },
            AssertOnlyLockConflictsAsync);

        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
        Assert.Equal(Messages, runs.Runs.Count);

        Assert.Equal(accounts.Length, runs.Runs.Select(run => run.InstanceId).Distinct().Count());
        Assert.All(stored, account =>
        {
            var state = Assert.IsType<AccountState>(account.StateData);
            Assert.Equal(MovementsPerAccount, state.Movements);
            Assert.Equal(ExpectedBalance, state.Balance);
        });
    }

    private static readonly int ExpectedBalance = MovementsOf(Guid.Empty.ToString())
        .Sum(movement => movement is Deposit deposit ? deposit.Amount : -((Withdraw)movement).Amount);

    // deposits and withdrawals travel on queues, or topics, of their own: they reach the hosts independently.
    // No two amounts are the same, so a movement applied twice cannot make up for one that was lost
    private static IEnumerable<IMessage> MovementsOf(string account)
        => Enumerable.Range(0, MovementsPerAccount).Select<int, IMessage>(index => index % 2 == 0
            ? new Deposit(account, 1 << index)
            : new Withdraw(account, 1 << index));

    private static Task PublishFromEveryHostAsync(ScenarioRun run, IEnumerable<IMessage> messages, CancellationToken cancellationToken)
    {
        var hosts = run.Hosts;
        return Task.WhenAll(messages.Select((message, index)
            => hosts[index % hosts.Count].PublishNewAsync(message, cancellationToken)));
    }
}
