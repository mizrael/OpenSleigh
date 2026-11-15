using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the relay cannot read its outbox for a while, as when the database does not answer: it is still there afterwards.
/// </summary>
public class OutboxReadFailureScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int FailedPassesToWaitFor = 3;

    // the relay of these tests reads its outbox every 250 ms, and waits that long after a pass that failed.
    // One that tried again without waiting would be back within a few milliseconds
    private static readonly TimeSpan ShortestPauseOfARelayThatBacksOff = TimeSpan.FromMilliseconds(200);

    // one host: a relay that stops for good after a failed pass is the relay of one host, however many there are
    [Theory]
    [MatrixData(HostsCounts = [1])]
    public async Task a_relay_that_cannot_read_its_outbox_keeps_trying_and_relays_what_is_in_it_afterwards(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var outboxCannotBeRead = new Fault("the outbox cannot be read, on purpose.");
        var runs = new HandlerRuns();

        var setup = new HostSetup(bus => bus.AddSaga<ShipmentSaga, ShipmentState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .FailOutboxReadsWhile(outboxCannotBeRead)
        };

        // on before the hosts start: a pass of the relay that had begun to read would not see the fault
        outboxCannotBeRead.Start();

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishNewAsync(new StartShipment(correlationId), cancellationToken);

            var failedPasses = outboxCannotBeRead.Hits.Count + FailedPassesToWaitFor;
            await run.WaitUntilAsync($"the relay to fail to read its outbox {FailedPassesToWaitFor} more times",
                () => outboxCannotBeRead.Hits.Count >= failedPasses, cancellationToken);
            Assert.Empty(runs.Runs);

            outboxCannotBeRead.Stop();
            await run.WaitForReleasedSagaAsync<ShipmentSaga>(correlationId, handledMessages: 1, cancellationToken);
        });

        Assert.Single(runs.Runs);

        var shortestPause = outboxCannotBeRead.ShortestTimeBetweenHitsOfOneSource();
        Assert.True(shortestPause >= ShortestPauseOfARelayThatBacksOff,
            $"the relay read its outbox again {shortestPause?.TotalMilliseconds} ms after it failed: it does not back off.");
    }
}
