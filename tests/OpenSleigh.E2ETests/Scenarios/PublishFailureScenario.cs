using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Transport;
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

    // in memory, where there is one host, and with a few hosts everywhere else: every relay finds the broker down,
    // and only one of them may publish the message when it is back
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_message_that_cannot_be_published_stays_in_the_outbox_and_is_delivered_once_afterwards(string combination, int hostsCount)
        => PublishWhileTheBrokerIsDownAsync(combination, hostsCount);

    [Theory]
    [MatrixData(HostsCounts = [3])]
    public Task a_message_that_cannot_be_published_stays_in_the_outbox_and_is_delivered_once_afterwards_with_several_hosts(string combination, int hostsCount)
        => PublishWhileTheBrokerIsDownAsync(combination, hostsCount);

    private async Task PublishWhileTheBrokerIsDownAsync(string combination, int hostsCount)
    {
        var brokerIsDown = new Fault("the broker cannot be reached, on purpose.");
        var published = new Expectation(1);
        var handled = new Expectation(1);
        Action<IMessageContext<StartSimpleSaga>> onMessage = _ => handled.Signal();

        var setup = new HostSetup(bus => bus.AddSaga<SimpleSaga>())
        {
            ConfigureServices = services => services
                .AddSingleton(onMessage)
                .ExpireOutboxClaimsAfter(MongoOutboxClaim)
                .ObservePublishedMessages(_ => published.Signal())
                .FailPublishingWhile(brokerIsDown)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            brokerIsDown.Start();
            await run.AnyHost.PublishAsync(new StartSimpleSaga(), cancellationToken);

            // the relay keeps trying, and the message stays where it is. One more failure than there are hosts:
            // at least one of them has then tried twice
            await brokerIsDown.WaitForHitsAsync(Math.Max(FailedAttemptsToWaitFor, hostsCount + 1), cancellationToken);
            Assert.Equal(1, await run.CountOutboxMessagesAsync(cancellationToken));
            Assert.Equal(0, handled.Count);

            brokerIsDown.Stop();
            await handled.Reached.WaitAsync(cancellationToken);
        });

        Assert.Equal(1, handled.Count);
        Assert.Equal(1, published.Count);

        var shortestPause = brokerIsDown.ShortestTimeBetweenHitsOfOneSource();
        Console.WriteLine($"MEASURED publish-failure attempts={brokerIsDown.Hits.Count} shortest-pause-ms={shortestPause?.TotalMilliseconds:F0}");
        Assert.True(shortestPause >= ShortestPauseOfARelayThatBacksOff,
            $"a relay tried to publish again {shortestPause?.TotalMilliseconds} ms after it failed: it does not back off.");
    }
}
