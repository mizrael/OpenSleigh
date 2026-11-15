using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the connections of the hosts to RabbitMQ are closed while a handler is running: the message it is handling
/// cannot be acknowledged any more, and the broker delivers it again.
/// </summary>
public class RabbitMqConnectionLossInFlightScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int MessagesOfAnInvoice = 2;

    // one store: it is the broker that goes away here, and the store takes no part in it.
    // Two hosts: the second delivery can reach the one that did not handle the first
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task a_message_that_was_being_handled_when_the_connections_were_closed_is_delivered_again_and_handled_once(string combination, int hostsCount)
    {
        var connectionName = $"e2e-{Guid.NewGuid():N}";
        var connectionLog = new RabbitMqConnectionLog();
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns();

        // the handler and, once the broker has delivered the message again, a host that finds it already handled
        var deliveredAgain = new Expectation(2);

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .NameRabbitMqConnections(connectionName)
                .LogRabbitMqConnections(connectionLog)
                .ObserveDeliveriesOf<SendInvoice>(deliveredAgain)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await using var connections = RabbitMqConnections.Named(connectionName);

                // the handler that sends the invoice waits until the connections are closed: its message is unacknowledged
                var sending = runs.HoldNext(handlerRun => handlerRun.MessageType == typeof(SendInvoice));
                await run.AnyHost.PublishNewAsync(new IssueInvoice(correlationId), cancellationToken);
                await run.WaitForAsync("the invoice to reach the handler that sends it", sending.Entered, cancellationToken);

                var listed = connections.WaitForAsync(hostsCount, cancellationToken);
                await run.WaitForAsync("the broker to list the connections of the hosts", listed, cancellationToken);
                var open = await listed;
                await run.WaitForAsync("the broker to close the connections of the hosts",
                    connections.CloseAsync(open, cancellationToken), cancellationToken);
                sending.Release();

                var invoice = await run.WaitForReleasedSagaAsync<InvoiceSaga>(correlationId, MessagesOfAnInvoice, cancellationToken);
                Assert.True(invoice.IsCompleted);
                await run.WaitForAsync("the message that could not be acknowledged to be delivered again", deliveredAgain.Reached, cancellationToken);
            },
            // the second delivery can arrive while the first is still being handled, and find the saga locked
            AssertOnlyLockConflictsAsync);

        await connectionLog.CloseAllAsync();

        // the handler ran once, whoever the message was delivered to afterwards, and however many times:
        // a host whose connection is closed while it acknowledges the second delivery is given a third
        Assert.True(deliveredAgain.Count >= 2);
        Assert.Single(runs.RunsOf<SendInvoice>());
    }
}
