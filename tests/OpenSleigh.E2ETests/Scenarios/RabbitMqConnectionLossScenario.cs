using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the connections of the hosts to RabbitMQ are closed under them, as a network blip or a broker failover does:
/// they have to publish and to consume again, without being restarted.
/// </summary>
public class RabbitMqConnectionLossScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int InvoicesEachTimeTheConnectionsAreClosed = 5;
    private const int MessagesOfAnInvoice = 2;

    // one store: it is the broker that goes away here, and the store takes no part in it.
    // Two hosts, which share their queues
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task hosts_publish_and_consume_again_without_a_restart_when_their_connections_are_closed(string combination, int hostsCount)
    {
        var outcome = await CloseTheConnectionsOfHostsThatAreIdleAsync(combination, hostsCount, times: 1);

        // every invoice was issued and sent once: nothing was lost with the connections, and nothing was handled twice
        Assert.Equal(MessagesOfAnInvoice * (1 + InvoicesEachTimeTheConnectionsAreClosed), outcome.Runs.HandledMessages);
        Assert.All(outcome.Runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
    }

    // twice: a host that ends up with one connection more every time is not seen by losing them once
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task hosts_have_as_many_connections_as_before_once_they_are_connected_again(string combination, int hostsCount)
    {
        var outcome = await CloseTheConnectionsOfHostsThatAreIdleAsync(combination, hostsCount, times: 2);

        Assert.All(outcome.OpenConnectionsAfterEachTime, open => Assert.Equal(outcome.OpenConnectionsBefore, open));
    }

    private sealed record Outcome(HandlerRuns Runs, int OpenConnectionsBefore, IReadOnlyList<int> OpenConnectionsAfterEachTime);

    private async Task<Outcome> CloseTheConnectionsOfHostsThatAreIdleAsync(string combination, int hostsCount, int times)
    {
        var connectionName = $"e2e-{Guid.NewGuid():N}";
        var connectionLog = new RabbitMqConnectionLog();
        var runs = new HandlerRuns();

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .NameRabbitMqConnections(connectionName)
                .LogRabbitMqConnections(connectionLog)
        };

        var openBefore = 0;
        var openAfterEachTime = new List<int>();

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                using var connections = RabbitMqConnections.Named(connectionName);

                // the hosts have published and consumed: whatever they connect for, they are connected
                var first = Guid.NewGuid().ToString();
                await run.AnyHost.PublishNewAsync(new IssueInvoice(first), cancellationToken);
                await run.WaitForReleasedSagaAsync<InvoiceSaga>(first, MessagesOfAnInvoice, cancellationToken);

                openBefore = connectionLog.Open;

                for (var time = 1; time <= times; time++)
                {
                    // every connection the hosts have open, once the broker lists them all
                    var listed = connections.WaitForAsync(Math.Max(hostsCount, connectionLog.Open), cancellationToken);
                    await run.WaitForAsync("the broker to list the connections of the hosts", listed, cancellationToken);
                    var open = await listed;
                    Assert.True(open.Count >= hostsCount);
                    await run.WaitForAsync("the broker to close the connections of the hosts",
                        connections.CloseAsync(open, cancellationToken), cancellationToken);

                    // published right away, one after the other, to hosts that may not have noticed yet:
                    // the queues are still there, without their consumers, and what is published meanwhile waits in them
                    var invoices = Enumerable.Range(0, InvoicesEachTimeTheConnectionsAreClosed).Select(_ => Guid.NewGuid().ToString()).ToArray();
                    foreach (var invoice in invoices)
                        await run.AnyHost.PublishNewAsync(new IssueInvoice(invoice), cancellationToken);

                    // the hosts consume again, and they publish again: the invoices are sent by a message of their own
                    await run.WaitForAsync("every host to consume from its queues again",
                        run.WaitUntilSubscribedAsync(cancellationToken), cancellationToken);
                    foreach (var invoice in invoices)
                        await run.WaitForReleasedSagaAsync<InvoiceSaga>(invoice, MessagesOfAnInvoice, cancellationToken);

                    openAfterEachTime.Add(connectionLog.Open);
                }
            },
            // a message that is delivered again while its first delivery is still being handled finds the saga locked
            AssertOnlyLockConflictsAsync);

        await connectionLog.CloseAllAsync();

        return new Outcome(runs, openBefore, openAfterEachTime);
    }
}
