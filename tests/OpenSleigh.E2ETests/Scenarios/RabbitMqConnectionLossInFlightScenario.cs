using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the connection of a host to RabbitMQ is closed while a handler is running: the message it is handling
/// cannot be acknowledged any more, and the broker delivers it again.
/// </summary>
public class RabbitMqConnectionLossInFlightScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int MessagesOfAnInvoice = 2;

    // one store: it is the broker that goes away here, and the store takes no part in it. With one host the message
    // comes back to the host that was handling it, once it is connected again. With three it is delivered at once to
    // a host that is still connected, which finds the saga locked and sends the message to the retry queue, on a
    // connection that is about to be closed as well. Which connection the broker closes first is not the scenario's
    // to say: RabbitMqConnectionLossDuringRetryScenario closes them at that very moment
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [1, 3])]
    public async Task a_message_that_was_being_handled_when_the_connection_was_closed_is_delivered_again_and_handled_once(string combination, int hostsCount)
    {
        await using var connectionLog = new RabbitMqConnectionLog(Console);
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns();

        // the handler and, once the broker has delivered the message again, the host that finds it already handled
        var deliveredAgain = new Expectation(2);

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .TrackRabbitMqConnections(connectionLog)
                .ObserveDeliveriesOf<SendInvoice>(deliveredAgain)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                using var connections = RabbitMqConnections.Of(connectionLog);

                // the handler that sends the invoice waits until the connection is closed: its message is unacknowledged
                var sending = runs.HoldNext(handlerRun => handlerRun.MessageType == typeof(SendInvoice));
                await run.AnyHost.PublishNewAsync(new IssueInvoice(correlationId), cancellationToken);
                await run.WaitForAsync("the invoice to reach the handler that sends it", sending.Entered, cancellationToken);

                var open = await run.WaitForAsync("the broker to list the connections of the host",
                    listing => connections.WaitForAsync(hostsCount, listing), cancellationToken);
                var closuresBefore = connectionLog.Closures;
                await connections.CloseAsync(open, cancellationToken);

                // the handler goes on as soon as its host has seen the connection go, before the connection is recovered:
                // what the broker lists lags behind both
                await run.WaitUntilAsync("the host to see its connection closed",
                    () => connectionLog.Closures >= closuresBefore + open.Count, cancellationToken);
                sending.Release();

                var invoice = await run.WaitForReleasedSagaAsync<InvoiceSaga>(correlationId, MessagesOfAnInvoice, cancellationToken);
                Assert.True(invoice.IsCompleted);
                await run.WaitForAsync("the message that could not be acknowledged to be delivered again", deliveredAgain.Reached, cancellationToken);
            },
            // the delivery that comes back finds the saga locked for as long as the first one is being handled and stored
            AssertOnlyLockConflictsAsync);

        // the handler ran once, however many times the message was delivered afterwards
        Assert.True(deliveredAgain.Count >= 2);
        Assert.Single(runs.RunsOf<SendInvoice>());
    }
}
