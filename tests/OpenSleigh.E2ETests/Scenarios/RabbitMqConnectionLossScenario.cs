using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the connections of the hosts to RabbitMQ are closed under them, as a network blip or a broker failover does:
/// every one of them has to publish and to consume again, without being restarted.
/// </summary>
public class RabbitMqConnectionLossScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int MessagesOfAnInvoice = 2;

    // one store: it is the broker that goes away here, and the store takes no part in it.
    // Two hosts, which share their queues and their outbox
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task every_host_publishes_and_consumes_again_without_a_restart_when_its_connection_is_closed(string combination, int hostsCount)
    {
        var outcome = await CloseTheConnectionsOfHostsThatAreIdleAsync(combination, hostsCount, times: 1);

        // every invoice was issued and sent once: nothing was lost with the connections, and nothing was handled twice
        Assert.Equal(MessagesOfAnInvoice * outcome.Invoices, outcome.Runs.HandledMessages);
        Assert.All(outcome.Runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
    }

    // the client recovers the connection after this long: time for the relay, which reads its outbox every 250 ms,
    // to find it closed more than once
    private static readonly TimeSpan RecoveryDelay = TimeSpan.FromSeconds(3);
    private const int FailedRelayPassesToWaitFor = 2;

    // one host: its relay is the only one that can publish the message, and its connection the only one there is
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [1])]
    public async Task a_message_published_while_the_connection_is_lost_stays_in_the_outbox_and_is_handled_once_when_it_is_back(string combination, int hostsCount)
    {
        var connectionLog = new RabbitMqConnectionLog();
        var runs = new HandlerRuns();
        var activity = new HostActivity();

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .RecordHostActivity(activity)
                .TrackRabbitMqConnections(connectionLog, recoverAfter: RecoveryDelay)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                using var connections = RabbitMqConnections.Of(connectionLog);
                var host = run.AnyHost;

                // the host has published and consumed: its channels and its consumers are there to be recovered
                var before = Guid.NewGuid().ToString();
                await host.PublishNewAsync(new IssueInvoice(before), cancellationToken);
                await run.WaitForReleasedSagaAsync<InvoiceSaga>(before, MessagesOfAnInvoice, cancellationToken);

                var open = await run.WaitForAsync("the broker to list the connection of the host",
                    listing => connections.WaitForAsync(hostsCount, listing), cancellationToken);
                var closuresBefore = connectionLog.Closures;
                await connections.CloseAsync(open, cancellationToken);
                await run.WaitUntilAsync("the host to see its connection closed",
                    () => connectionLog.Closures >= closuresBefore + open.Count, cancellationToken);

                activity.StartOver();
                var meanwhile = Guid.NewGuid().ToString();
                await host.PublishNewAsync(new IssueInvoice(meanwhile), cancellationToken);

                await run.WaitUntilAsync($"the relay to find the connection closed {FailedRelayPassesToWaitFor} times",
                    () => activity.FailedRelayPasses(host) >= FailedRelayPassesToWaitFor, cancellationToken);
                Assert.False(activity.HasPublished(host));
                Assert.Equal(1, await run.CountOutboxMessagesAsync(cancellationToken));

                // on the connection it had, and on no other: the consumers are the ones the client recovered
                await run.WaitForReleasedSagaAsync<InvoiceSaga>(meanwhile, MessagesOfAnInvoice, cancellationToken);
                Assert.Equal(hostsCount, connectionLog.Opened);
                Assert.Equal(hostsCount, connectionLog.Open);
            },
            // a message that is delivered again while its first delivery is still being handled finds the saga locked
            AssertOnlyLockConflictsAsync);

        Assert.Equal(2 * MessagesOfAnInvoice, runs.HandledMessages);
        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
    }

    // twice: a host that ends up with one connection more every time is not seen by losing them once.
    // And stopped: a host that opens a connection when its own is closed leaves it behind
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task hosts_have_as_many_connections_as_before_once_they_are_connected_again(string combination, int hostsCount)
    {
        var outcome = await CloseTheConnectionsOfHostsThatAreIdleAsync(combination, hostsCount, times: 2);

        Console.WriteLine(
            $"connections of {hostsCount} hosts: {outcome.OpenConnectionsBefore} open before, " +
            $"{string.Join(" then ", outcome.OpenConnectionsAfterEachTime)} open after each loss, " +
            $"{outcome.OpenConnectionsAfterStop} open once stopped, {outcome.OpenedConnections} opened in all.");

        Assert.All(outcome.OpenConnectionsAfterEachTime, open => Assert.Equal(outcome.OpenConnectionsBefore, open));
        Assert.Equal(outcome.OpenConnectionsBefore, outcome.OpenedConnections);
        Assert.Equal(0, outcome.OpenConnectionsAfterStop);
    }

    private sealed record Outcome(
        HandlerRuns Runs,
        int Invoices,
        int OpenConnectionsBefore,
        IReadOnlyList<int> OpenConnectionsAfterEachTime,
        int OpenConnectionsAfterStop,
        int OpenedConnections);

    private async Task<Outcome> CloseTheConnectionsOfHostsThatAreIdleAsync(string combination, int hostsCount, int times)
    {
        var connectionLog = new RabbitMqConnectionLog();
        var runs = new HandlerRuns();
        var activity = new HostActivity();

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .RecordHostActivity(activity)
                .TrackRabbitMqConnections(connectionLog)
        };

        var invoices = 0;
        var openBefore = 0;
        var openAfterEachTime = new List<int>();

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                using var connections = RabbitMqConnections.Of(connectionLog);

                // the hosts share their outbox and their queues: invoices that complete do not tell that every host
                // works. Invoices are issued, one at a time and through each host in turn, until each host has taken
                // one, and its relay has published a message, and it has processed one it was delivered
                async Task IssueInvoicesUntilEveryHostHasPublishedAndConsumedAsync(CancellationToken issuing)
                {
                    activity.StartOver();

                    bool EveryHostHasPublishedAndConsumed()
                        => run.Hosts.All(host => activity.HasPublished(host) && activity.HasProcessed(host));

                    for (var turn = 0; turn < run.Hosts.Count || !EveryHostHasPublishedAndConsumed(); turn++)
                    {
                        var invoice = Guid.NewGuid().ToString();
                        await run.Hosts[turn % run.Hosts.Count].PublishNewAsync(new IssueInvoice(invoice), issuing);
                        await run.WaitForReleasedSagaAsync<InvoiceSaga>(invoice, MessagesOfAnInvoice, issuing);
                        invoices++;
                    }
                }

                await run.WaitForAsync("every host to publish and to consume a message",
                    IssueInvoicesUntilEveryHostHasPublishedAndConsumedAsync, cancellationToken);
                openBefore = connectionLog.Open;

                for (var time = 1; time <= times; time++)
                {
                    // every connection the hosts have open, once the broker lists them all
                    var open = await run.WaitForAsync("the broker to list the connections of the hosts",
                        listing => connections.WaitForAsync(Math.Max(hostsCount, connectionLog.Open), listing), cancellationToken);
                    Assert.True(open.Count >= hostsCount);

                    await connections.CloseAsync(open, cancellationToken);
                    await run.WaitForAsync("the broker to have closed the connections of the hosts",
                        closing => connections.WaitUntilClosedAsync(open, closing), cancellationToken);

                    // a relay that publishes on a connection the broker is closing is told nothing, and removes
                    // the message from the outbox: the invoices are issued once the hosts are connected again
                    await run.WaitForAsync("every host to consume from its queues again",
                        run.WaitUntilSubscribedAsync, cancellationToken);

                    await run.WaitForAsync("every host to publish and to consume a message again, after its connection was closed",
                        IssueInvoicesUntilEveryHostHasPublishedAndConsumedAsync, cancellationToken);

                    openAfterEachTime.Add(connectionLog.Open);
                }
            },
            // a message that is delivered again while its first delivery is still being handled finds the saga locked
            AssertOnlyLockConflictsAsync);

        // the hosts are stopped by now
        return new Outcome(runs, invoices, openBefore, openAfterEachTime, connectionLog.Open, connectionLog.Opened);
    }
}
