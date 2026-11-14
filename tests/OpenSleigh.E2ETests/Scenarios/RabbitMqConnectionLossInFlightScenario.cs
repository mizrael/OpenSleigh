using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the connections of the hosts to RabbitMQ are closed while a handler is running: the message it is handling
/// cannot be acknowledged any more, and the broker delivers it again.
/// </summary>
public class RabbitMqConnectionLossInFlightScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // one store: it is the broker that goes away here, and the store takes no part in it.
    // Two hosts: the second delivery can reach the one that did not handle the first
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task a_message_that_was_being_handled_when_the_connections_were_closed_is_delivered_again_and_handled_once(string combination, int hostsCount)
    {
        var connectionName = $"e2e-{Guid.NewGuid():N}";
        var correlationId = Guid.NewGuid().ToString();
        var handlerIsRunning = new Expectation(1);
        var connectionsAreClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new Expectation(1);

        // the handler and, once the broker has delivered the message again, a host that finds it already handled
        var deliveredAgain = new Expectation(2);

        Action<IMessageContext<SendInvoice>> onSent = _ => sent.Signal();
        WhileSendingInvoice whileSending = async (_, cancellationToken) =>
        {
            handlerIsRunning.Signal();
            await connectionsAreClosed.Task.WaitAsync(cancellationToken);
        };

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services => services
                .AddSingleton(onSent)
                .AddSingleton(whileSending)
                .NameRabbitMqConnections(connectionName)
                .ObserveDeliveriesOf<SendInvoice>(deliveredAgain)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                using var connections = RabbitMqConnections.Named(connectionName);

                await run.AnyHost.PublishAsync(new IssueInvoice(correlationId), cancellationToken);
                await handlerIsRunning.Reached.WaitAsync(cancellationToken);

                var open = await connections.WaitForAsync(hostsCount, cancellationToken);
                await connections.CloseAsync(open, cancellationToken);
                var closed = Stopwatch.StartNew();

                connectionsAreClosed.SetResult();

                await run.WaitForSagaAsync<InvoiceSaga>(correlationId, stored => stored.IsCompleted, cancellationToken);
                await deliveredAgain.Reached.WaitAsync(cancellationToken);

                Console.WriteLine($"MEASURED rabbitmq-connection-loss-in-flight delivered-again-ms={closed.ElapsedMilliseconds}");
            },
            async run =>
            {
                await AssertNoDeadLettersAsync(run);

                // the second delivery can arrive while the first is still being handled, and find the saga locked
                Assert.All(run.ProcessingFailures, failure => Assert.True(failure.IsLockConflict, failure.Exception.ToString()));
            });

        // the handler ran once, whoever the message was delivered to afterwards, and however many times:
        // a host whose connection is closed while it acknowledges the second delivery is given a third
        Assert.Equal(1, sent.Count);
        Assert.True(deliveredAgain.Count >= 2);
    }
}
