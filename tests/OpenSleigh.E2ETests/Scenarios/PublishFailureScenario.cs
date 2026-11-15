using System.Collections.Concurrent;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the broker cannot be published to for a while: what the hosts have to send waits in the outbox,
/// and a relay that could not publish waits before its next pass.
/// </summary>
public class PublishFailureScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int FailedAttemptsToWaitFor = 2;

    // the relay of these tests reads its outbox every 250 ms, and waits that long after a pass that failed.
    // One that went on without waiting would be back within a few milliseconds
    private static readonly TimeSpan ShortestPauseOfARelayThatBacksOff = TimeSpan.FromMilliseconds(200);

    // on MongoDB a message that could not be published is read again when its claim expires: soon enough here
    // for it to be tried a second time, and for the message to go out shortly after the broker is back
    private static readonly TimeSpan MongoOutboxClaim = TimeSpan.FromMilliseconds(500);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_message_that_cannot_be_published_stays_in_the_outbox_and_is_published_once_afterwards(string combination, int hostsCount)
        => PublishWhileTheBrokerIsDownAsync(combination, hostsCount);

    // every relay finds the broker down, and only one of them may publish the message when it is back.
    // The publisher fails before the transport is asked, and what keeps the message is the outbox of the store:
    // every store runs it, and one combination has Kafka
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [3])]
    public Task a_message_that_cannot_be_published_stays_in_the_outbox_and_is_published_once_afterwards_with_several_hosts(string combination, int hostsCount)
        => PublishWhileTheBrokerIsDownAsync(combination, hostsCount);

    private async Task PublishWhileTheBrokerIsDownAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var brokerIsDown = new Fault("the broker cannot be reached, on purpose.");
        var runs = new HandlerRuns();
        var activity = new HostActivity();
        var published = new ConcurrentQueue<MessageEnvelope>();

        var setup = new HostSetup(bus => bus.AddSaga<ShipmentSaga, ShipmentState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .RecordHostActivity(activity)
                .ExpireOutboxClaimsAfter(MongoOutboxClaim)
                .ObservePublishedMessages(published.Enqueue)
                .FailPublishingWhile(brokerIsDown)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            brokerIsDown.Start();
            await run.AnyHost.PublishNewAsync(new StartShipment(correlationId), cancellationToken);

            // the message is tried again, and stays where it is. How long a relay waits after a pass that failed
            // shows in its next pass, whatever that one finds: on MongoDB the message is claimed, and it finds nothing
            await run.WaitUntilAsync(
                $"the relays to fail to publish the message {FailedAttemptsToWaitFor} times, and one of them to have had a pass after one that failed",
                () => brokerIsDown.Hits >= FailedAttemptsToWaitFor && activity.ShortestPauseAfterAFailedRelayPass() is not null,
                cancellationToken);
            Assert.Equal(1, await run.CountOutboxMessagesAsync(cancellationToken));
            Assert.Empty(published);
            Assert.Empty(runs.Runs);

            brokerIsDown.Stop();
            await run.WaitForReleasedSagaAsync<ShipmentSaga>(correlationId, handledMessages: 1, cancellationToken);
        });

        // also after the observation window: one relay published the message, and it was handled once
        Assert.Single(published);
        Assert.Single(runs.Runs);

        var shortestPause = activity.ShortestPauseAfterAFailedRelayPass();
        Assert.True(shortestPause >= ShortestPauseOfARelayThatBacksOff,
            $"a relay read its outbox again {shortestPause?.TotalMilliseconds} ms after a pass that could not publish: it does not back off.");
    }
}
