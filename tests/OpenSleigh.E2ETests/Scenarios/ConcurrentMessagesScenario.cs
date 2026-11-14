using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
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
    private const int Accounts = 3;
    private const int MovementsPerAccount = 12;
    private const int MessagesPerAccount = 1 + MovementsPerAccount;

    // one host, for the messages a single host handles at the same time, and a few, for the hosts that compete.
    // A pair adds nothing that five hosts do not show
    [Theory]
    [MatrixData(HostsCounts = [1, 5])]
    public async Task every_message_for_one_saga_is_handled_exactly_once(string combination, int hostsCount)
    {
        var accounts = Enumerable.Range(0, Accounts).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var runs = new HandlerRuns(Accounts * MessagesPerAccount);

        var setup = new HostSetup(bus => bus.AddSaga<AccountSaga, AccountState>())
        {
            ConfigureServices = services => services.AddSingleton(runs)
        };

        var stored = new List<SagaInstanceInfo>();

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                // a movement that reached a host before its account is stored would be dead-lettered, by design:
                // the accounts are opened first, so that no movement can be early and every dead letter is a failure
                foreach (var account in accounts)
                    await PublishAsync(run.AnyHost, new OpenAccount(account), cancellationToken);
                foreach (var account in accounts)
                    await WaitForAccountAsync(run, account, handledMessages: 1, cancellationToken);

                var hosts = run.Hosts;
                var movements = accounts.SelectMany(MovementsOf).ToArray();
                await Task.WhenAll(movements.Select((movement, index)
                    => PublishAsync(hosts[index % hosts.Count], movement, cancellationToken)));

                await runs.AllHandled.WaitAsync(cancellationToken);

                foreach (var account in accounts)
                    stored.Add(await WaitForAccountAsync(run, account, MessagesPerAccount, cancellationToken));
            },
            async run =>
            {
                await AssertNoDeadLettersAsync(run);

                // the only reason a host gives a message back is a saga that someone else holds
                Assert.All(run.ProcessingFailures, failure => Assert.True(failure.IsLockConflict, failure.ToString()));
            });

        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
        Assert.Equal(Accounts * MessagesPerAccount, runs.Runs.Count);

        // one instance per account, and every movement in its balance, once
        Assert.Equal(Accounts, runs.Runs.Select(run => run.InstanceId).Distinct().Count());
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

    private static Task<SagaInstanceInfo> WaitForAccountAsync(
        ScenarioRun run, string account, int handledMessages, CancellationToken cancellationToken)
        => run.WaitForSagaAsync<AccountSaga>(account,
            saga => saga.ProcessedMessages.Count == handledMessages && !saga.IsLocked,
            cancellationToken);

    private static async Task PublishAsync(ScenarioHost host, IMessage message, CancellationToken cancellationToken)
        => Assert.Equal(OutboxAppendResult.Success, await host.PublishAsync(message, cancellationToken));
}
