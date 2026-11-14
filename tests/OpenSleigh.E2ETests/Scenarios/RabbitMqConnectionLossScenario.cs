using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Transport;
using OpenSleigh.Transport.RabbitMQ;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the connections of the hosts to RabbitMQ are closed under them, as a network blip or a broker failover does:
/// they have to publish and to consume again, without being restarted.
/// </summary>
public class RabbitMqConnectionLossScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int InvoicesAfterTheConnectionsAreClosed = 5;

    // one store: it is the broker that goes away here, and the store takes no part in it.
    // Two hosts, which share their queues
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task hosts_publish_and_consume_again_without_a_restart_when_their_connections_are_closed(string combination, int hostsCount)
    {
        var outcome = await CloseTheConnectionsOfHostsThatAreIdleAsync(combination, hostsCount);

        // each invoice was sent once: nothing was lost with the connections, and nothing was handled twice
        Assert.Equal(1 + InvoicesAfterTheConnectionsAreClosed, outcome.TimesSent.Count);
        Assert.All(outcome.TimesSent, invoice => Assert.Equal(1, invoice.Value));
    }

    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task hosts_have_as_many_connections_as_before_once_they_are_connected_again(string combination, int hostsCount)
    {
        var outcome = await CloseTheConnectionsOfHostsThatAreIdleAsync(combination, hostsCount);

        Assert.Equal(outcome.ConnectionsBefore, outcome.ConnectionsAfter);
    }

    private sealed record Outcome(IReadOnlyDictionary<string, int> TimesSent, int ConnectionsBefore, int ConnectionsAfter);

    private async Task<Outcome> CloseTheConnectionsOfHostsThatAreIdleAsync(string combination, int hostsCount)
    {
        var connectionName = $"e2e-{Guid.NewGuid():N}";
        var timesSent = new ConcurrentDictionary<string, int>();
        Action<IMessageContext<SendInvoice>> onSent = context => timesSent.AddOrUpdate(context.CorrelationId, 1, (_, times) => times + 1);
        WhileSendingInvoice whileSending = (_, _) => Task.CompletedTask;

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services => services
                .AddSingleton(onSent)
                .AddSingleton(whileSending)
                .NameRabbitMqConnections(connectionName)
        };

        var connectionsBefore = 0;
        var connectionsAfter = 0;

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                using var connections = RabbitMqConnections.Named(connectionName);

                Task WaitUntilSentAsync(string correlationId)
                    => run.WaitForSagaAsync<InvoiceSaga>(correlationId, stored => stored.IsCompleted, cancellationToken);

                // the hosts have published and consumed: whatever they connect for, they are connected
                var first = Guid.NewGuid().ToString();
                await run.AnyHost.PublishAsync(new IssueInvoice(first), cancellationToken);
                await WaitUntilSentAsync(first);

                var queueNames = QueueNamesOf(run.AnyHost);
                var open = await connections.WaitForAsync(hostsCount, cancellationToken);
                connectionsBefore = open.Count;

                await connections.CloseAsync(open, cancellationToken);
                var closed = Stopwatch.StartNew();

                // the queues are still there, without their consumers: what is published meanwhile waits in them
                foreach (var queueName in queueNames)
                    Assert.NotNull(await connections.CountConsumersAsync(queueName, cancellationToken));

                // published right away, to hosts that may not have noticed yet
                var invoices = Enumerable.Range(0, InvoicesAfterTheConnectionsAreClosed).Select(_ => Guid.NewGuid().ToString()).ToArray();
                foreach (var invoice in invoices)
                    await run.AnyHost.PublishAsync(new IssueInvoice(invoice), cancellationToken);

                await connections.WaitForConsumersAsync(queueNames, (uint)hostsCount, cancellationToken);
                var consumersBack = closed.Elapsed;

                await Task.WhenAll(invoices.Select(WaitUntilSentAsync));
                var allSent = closed.Elapsed;

                connectionsAfter = (await connections.ListAsync(cancellationToken)).Count;

                Console.WriteLine(
                    $"MEASURED rabbitmq-connection-loss connections-before={connectionsBefore} connections-after={connectionsAfter} " +
                    $"consumers-back-ms={consumersBack.TotalMilliseconds:F0} all-sent-ms={allSent.TotalMilliseconds:F0}");
            },
            async run =>
            {
                await AssertNoDeadLettersAsync(run);

                // a message that is delivered again while its first delivery is still being handled finds the saga locked
                Assert.All(run.ProcessingFailures, failure => Assert.True(failure.IsLockConflict, failure.Exception.ToString()));
            });

        return new Outcome(timesSent, connectionsBefore, connectionsAfter);
    }

    private static IReadOnlyCollection<string> QueueNamesOf(ScenarioHost host)
    {
        var queues = host.Services.GetRequiredService<IQueueReferenceFactory>();
        return host.Services.GetHandledMessageTypes().Select(messageType => queues.Create(messageType).QueueName).ToArray();
    }
}
