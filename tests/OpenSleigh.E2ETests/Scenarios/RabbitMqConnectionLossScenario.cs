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
    private const int InvoicesEachTimeTheConnectionsAreClosed = 5;

    // one store: it is the broker that goes away here, and the store takes no part in it.
    // Two hosts, which share their queues
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task hosts_publish_and_consume_again_without_a_restart_when_their_connections_are_closed(string combination, int hostsCount)
    {
        var outcome = await CloseTheConnectionsOfHostsThatAreIdleAsync(combination, hostsCount, times: 1);

        // each invoice was sent once: nothing was lost with the connections, and nothing was handled twice
        Assert.Equal(1 + InvoicesEachTimeTheConnectionsAreClosed, outcome.TimesSent.Count);
        Assert.All(outcome.TimesSent, invoice => Assert.Equal(1, invoice.Value));
    }

    // twice: a host that ends up with one connection more every time is not seen by losing them once
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [2])]
    public async Task hosts_have_as_many_connections_as_before_once_they_are_connected_again(string combination, int hostsCount)
    {
        var outcome = await CloseTheConnectionsOfHostsThatAreIdleAsync(combination, hostsCount, times: 2);

        Assert.All(outcome.OpenConnectionsAfterEachTime, open => Assert.Equal(outcome.OpenConnectionsBefore, open));
    }

    private sealed record Outcome(
        IReadOnlyDictionary<string, int> TimesSent,
        int OpenConnectionsBefore,
        IReadOnlyList<int> OpenConnectionsAfterEachTime);

    private async Task<Outcome> CloseTheConnectionsOfHostsThatAreIdleAsync(string combination, int hostsCount, int times)
    {
        var connectionName = $"e2e-{Guid.NewGuid():N}";
        var connectionLog = new RabbitMqConnectionLog();
        var timesSent = new ConcurrentDictionary<string, int>();
        Action<IMessageContext<SendInvoice>> onSent = context => timesSent.AddOrUpdate(context.CorrelationId, 1, (_, sent) => sent + 1);
        WhileSendingInvoice whileSending = (_, _) => Task.CompletedTask;

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga>())
        {
            ConfigureServices = services => services
                .AddSingleton(onSent)
                .AddSingleton(whileSending)
                .NameRabbitMqConnections(connectionName)
                .LogRabbitMqConnections(connectionLog)
        };

        var openBefore = 0;
        var openAfterEachTime = new List<int>();

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await using var connections = RabbitMqConnections.Named(connectionName);

                Task WaitUntilSentAsync(string correlationId)
                    => run.WaitForSagaAsync<InvoiceSaga>(correlationId, stored => stored.IsCompleted, cancellationToken);

                // the hosts have published and consumed: whatever they connect for, they are connected
                var first = Guid.NewGuid().ToString();
                await run.AnyHost.PublishAsync(new IssueInvoice(first), cancellationToken);
                await WaitUntilSentAsync(first);

                var queueNames = QueueNamesOf(run.AnyHost);
                openBefore = connectionLog.Open;

                for (var time = 1; time <= times; time++)
                {
                    // every connection the hosts have open, once the broker lists them all
                    var open = await connections.WaitForAsync(connectionLog.Open, cancellationToken);
                    await connections.CloseAsync(open, cancellationToken);
                    var closed = Stopwatch.StartNew();

                    // the queues are still there, without their consumers: what is published meanwhile waits in them
                    foreach (var queueName in queueNames)
                        Assert.NotNull(await connections.CountConsumersAsync(queueName, cancellationToken));

                    // published right away, to hosts that may not have noticed yet
                    var invoices = Enumerable.Range(0, InvoicesEachTimeTheConnectionsAreClosed).Select(_ => Guid.NewGuid().ToString()).ToArray();
                    foreach (var invoice in invoices)
                        await run.AnyHost.PublishAsync(new IssueInvoice(invoice), cancellationToken);

                    await connections.WaitForConsumersAsync(queueNames, (uint)hostsCount, cancellationToken);
                    var consumersBack = closed.Elapsed;

                    // the hosts consume again, and they publish again: the invoices are sent by a message of their own
                    await Task.WhenAll(invoices.Select(WaitUntilSentAsync));
                    var allSent = closed.Elapsed;

                    openAfterEachTime.Add(connectionLog.Open);

                    Console.WriteLine(
                        $"MEASURED rabbitmq-connection-loss time={time} closed={open.Count} open-before={openBefore} open-after={connectionLog.Open} " +
                        $"opened-in-all={connectionLog.Opened} consumers-back-ms={consumersBack.TotalMilliseconds:F0} all-sent-ms={allSent.TotalMilliseconds:F0}");
                }
            },
            async run =>
            {
                await AssertNoDeadLettersAsync(run);

                // a message that is delivered again while its first delivery is still being handled finds the saga locked
                Assert.All(run.ProcessingFailures, failure => Assert.True(failure.IsLockConflict, failure.Exception.ToString()));
            });

        await connectionLog.CloseAllAsync();

        return new Outcome(timesSent, openBefore, openAfterEachTime);
    }

    private static IReadOnlyCollection<string> QueueNamesOf(ScenarioHost host)
    {
        var queues = host.Services.GetRequiredService<IQueueReferenceFactory>();
        return host.Services.GetHandledMessageTypes().Select(messageType => queues.Create(messageType).QueueName).ToArray();
    }
}
