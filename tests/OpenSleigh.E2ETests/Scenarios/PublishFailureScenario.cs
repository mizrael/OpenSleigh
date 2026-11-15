using System.Collections.Concurrent;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the broker cannot be published to for a while: what the hosts have to send waits in the outbox.
/// </summary>
public class PublishFailureScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int FailedAttemptsToWaitFor = 3;

    // the relay of these tests reads its outbox every 250 ms, and waits that long after a pass that failed.
    // One that tried again without waiting would be back within a few milliseconds
    private static readonly TimeSpan ShortestPauseOfARelayThatBacksOff = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan MongoOutboxClaim = TimeSpan.FromSeconds(2);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_message_that_cannot_be_published_stays_in_the_outbox_and_is_published_once_afterwards(string combination, int hostsCount)
        => PublishWhileTheBrokerIsDownAsync(combination, hostsCount);

    // every relay finds the broker down, and only one of them may publish the message when it is back
    [Theory]
    [MatrixData(HostsCounts = [3], RequiresSharedInfrastructure = true)]
    public Task a_message_that_cannot_be_published_stays_in_the_outbox_and_is_published_once_afterwards_with_several_hosts(string combination, int hostsCount)
        => PublishWhileTheBrokerIsDownAsync(combination, hostsCount);

    private async Task PublishWhileTheBrokerIsDownAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var brokerIsDown = new Fault("the broker cannot be reached, on purpose.");
        var runs = new HandlerRuns();
        var published = new ConcurrentQueue<MessageEnvelope>();

        var setup = new HostSetup(bus => bus.AddSaga<ShipmentSaga, ShipmentState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .ExpireOutboxClaimsAfter(MongoOutboxClaim)
                .ObservePublishedMessages(published.Enqueue)
                .FailPublishingWhile(brokerIsDown)
        };

        // one more failure than there are hosts: at least one relay has then tried twice
        var failedAttemptsToWaitFor = Math.Max(FailedAttemptsToWaitFor, hostsCount + 1);

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            brokerIsDown.Start();
            await run.AnyHost.PublishNewAsync(new StartShipment(correlationId), cancellationToken);

            // the relays keep trying, and the message stays where it is
            await run.WaitUntilAsync($"the relays to fail to publish {failedAttemptsToWaitFor} times",
                () => brokerIsDown.Hits.Count >= failedAttemptsToWaitFor, cancellationToken);
            Assert.Equal(1, await run.CountOutboxMessagesAsync(cancellationToken));
            Assert.Empty(published);
            Assert.Empty(runs.Runs);

            brokerIsDown.Stop();
            await run.WaitForReleasedSagaAsync<ShipmentSaga>(correlationId, handledMessages: 1, cancellationToken);
        });

        // also after the observation window: one relay published the message, and it was handled once
        Assert.Single(published);
        Assert.Single(runs.Runs);

        var shortestPause = brokerIsDown.ShortestTimeBetweenHitsOfOneSource();
        Assert.True(shortestPause >= ShortestPauseOfARelayThatBacksOff,
            $"a relay tried to publish again {shortestPause?.TotalMilliseconds} ms after it failed: it does not back off.");
    }
}
