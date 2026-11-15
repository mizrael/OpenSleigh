using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// many instances of one saga in flight at the same time, each going through its steps on whatever host
/// gets its next message.
/// </summary>
public class ManySagasAtOnceScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int TransfersCount = 300;
    private const int HostsCount = 3;

    // what keeps the instances apart is code of the store (the lookup by correlation id, the lock, and on the SQL
    // stores the pooled contexts): every store runs it, on RabbitMQ. One combination has Kafka,
    // where the messages of an instance are spread over partitions and hosts
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [HostsCount])]
    public Task every_instance_ends_with_its_own_state_on_several_hosts(string combination, int hostsCount)
        => RunTransfersAsync(combination, hostsCount);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task every_instance_ends_with_its_own_state_in_memory(string combination, int hostsCount)
        => RunTransfersAsync(combination, hostsCount);

    private async Task RunTransfersAsync(string combination, int hostsCount)
    {
        var workload = new TransferWorkload(TransfersCount);

        var setup = new HostSetup(bus => bus.AddSaga<TransferSaga, TransferState>())
        {
            ConfigureServices = services => workload.AddTo(services)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await workload.OpenAllAsync(run.Hosts, cancellationToken);
                await workload.WaitUntilAllAreDebitedAsync(run, cancellationToken);

                // all of them are in flight now, none can end before its settlement: each is stored with what it did so far
                await workload.AssertEachIsStoredAsync(run, settled: false, cancellationToken);

                await workload.SettleAllAsync(run.Hosts, cancellationToken);
                await workload.WaitUntilAllAreSettledAsync(run, cancellationToken);
                await workload.AssertEachIsStoredAsync(run, settled: true, cancellationToken);
            },
            AssertOnlyLockConflictsAsync);

        workload.AssertEveryMessageWasHandledOnce();
    }
}
