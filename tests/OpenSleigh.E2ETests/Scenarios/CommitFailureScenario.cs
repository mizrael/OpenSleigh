using System.Collections.Concurrent;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a handler does its work and its saga cannot be committed, as when the database goes away at that moment:
/// nothing of what the handler did is kept, and the saga is free for the next message.
/// Nothing retries a message that failed for another reason than a lock: it is dead-lettered,
/// and the scenario asks again, as whoever looks after the dead letters would.
/// </summary>
public class CommitFailureScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int MessagesOfAShipmentThatIsPacked = 3;

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_commit_that_fails_keeps_nothing_of_what_the_handler_did_and_leaves_the_saga_unlocked(string combination, int hostsCount)
        => FailACommitAsync(combination, hostsCount);

    // the message that follows can reach another host, which only knows what is in the store. The commit fails before
    // the store is asked; what releases the saga afterwards is the store, and what dead-letters the message is
    // the transport: every store runs it, and one combination has Kafka
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [3])]
    public Task a_commit_that_fails_keeps_nothing_of_what_the_handler_did_and_leaves_the_saga_unlocked_with_several_hosts(string combination, int hostsCount)
        => FailACommitAsync(combination, hostsCount);

    private async Task FailACommitAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var commitFails = new Fault("the saga cannot be committed, on purpose.");
        var runs = new HandlerRuns();
        var published = new ConcurrentQueue<MessageEnvelope>();

        var setup = new HostSetup(bus => bus.AddSaga<ShipmentSaga, ShipmentState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .ObservePublishedMessages(published.Enqueue)
                .FailCommitsThatPublish<ParcelPacked>(commitFails)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishNewAsync(new StartShipment(correlationId), cancellationToken);
                await run.WaitForReleasedSagaAsync<ShipmentSaga>(correlationId, handledMessages: 1, cancellationToken);

                commitFails.StartFor(failures: 1);
                await run.AnyHost.PublishNewAsync(new PackParcel(correlationId), cancellationToken);

                // the host has given the message back to its transport: it is done with it
                await run.WaitUntilAsync("the commit to fail and its message to be given back",
                    () => commitFails.Hits >= 1 && run.ProcessingFailures.Any(IsTheFailedCommit),
                    cancellationToken, IsTheFailedCommit);
                Assert.Single(runs.RunsOf<PackParcel>());

                // a saga left locked would stay so until its lock expires, long after this scenario has given up.
                // It is stored as it was before the message: not handled, and the state untouched
                var afterFailedCommit = await run.WaitForReleasedSagaAsync<ShipmentSaga>(
                    correlationId, handledMessages: 1, cancellationToken, IsTheFailedCommit);
                Assert.Equal(0, Assert.IsType<ShipmentState>(afterFailedCommit.StateData).PackingAttempts);
                Assert.False(afterFailedCommit.IsCompleted);

                await run.AnyHost.PublishNewAsync(new PackParcel(correlationId), cancellationToken);

                var packed = await run.WaitForReleasedSagaAsync<ShipmentSaga>(
                    correlationId, MessagesOfAShipmentThatIsPacked, cancellationToken, IsTheFailedCommit);
                Assert.True(packed.IsCompleted);
                Assert.Equal(1, Assert.IsType<ShipmentState>(packed.StateData).PackingAttempts);
            },
            // the message whose commit failed is given back once and dead-lettered once, and nothing else is:
            // nobody found the saga locked afterwards
            async run =>
            {
                var failure = Assert.Single(run.ProcessingFailures);
                Assert.True(IsTheFailedCommit(failure), failure.ToString());

                await AssertDeadLettersAsync(run, IsTheFailedCommit);
            });

        // also after the observation window: the handler ran for both requests,
        // and what it published before the commit failed never went out
        Assert.Equal(2, runs.RunsOf<PackParcel>().Select(handlerRun => handlerRun.MessageId).Distinct().Count());
        Assert.Equal(2, runs.RunsOf<PackParcel>().Count);
        Assert.Single(published, message => message.MessageType == typeof(ParcelPacked));
        Assert.Single(runs.RunsOf<ParcelPacked>());
    }

    private static bool IsTheFailedCommit(ProcessingFailure failure)
        => failure.MessageType == typeof(PackParcel) && failure.Exception is InjectedFailureException;
}
