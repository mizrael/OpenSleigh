using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// delivery is at least once: a relay that stops between publishing a message and removing it from the outbox
/// publishes it again, and a broker delivers again what was not acknowledged in time. Here every message is
/// published several times at once, and then once more when the saga is long done with it.
/// </summary>
public class RedeliveryScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int CopiesAtOnce = 3;
    private const int DeliveriesOfEachMessage = 2 * CopiesAtOnce;
    private const int Messages = 2;

    // the copies reach different hosts at the same time, or one host after the other, as the transport has it.
    // One host of these transports shows nothing that five hosts do not
    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [5])]
    public Task a_message_delivered_again_is_not_handled_again_by_any_host(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    // in memory a host handles the copies at the same time
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_message_delivered_again_is_not_handled_again_by_a_host_that_handles_several_at_once(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    private async Task RunAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns(Messages);
        var deliveries = new Deliveries();
        var published = new ConcurrentDictionary<string, MessageEnvelope>();

        var setup = new HostSetup(bus => bus.AddSaga<InvoiceSaga, InvoiceState>())
        {
            ConfigureServices = services => services
                .AddSingleton(runs)
                .CountDeliveries(deliveries)
                .Decorate<IPublisher>((_, inner) => new RepeatingPublisher(inner, published))
        };

        SagaInstanceInfo? stored = null;

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishAsync(new IssueInvoice(correlationId), cancellationToken);

                // the copies of a message race each other, and the saga
                await deliveries.WaitUntilProcessedAsync<IssueInvoice>(CopiesAtOnce, cancellationToken);
                await deliveries.WaitUntilProcessedAsync<SendInvoiceEmail>(CopiesAtOnce, cancellationToken);
                await WaitForInvoiceAsync(run, correlationId, cancellationToken);

                // and come back when the saga has handled them and moved on, through another host when there is one
                var publisher = run.Hosts[^1].Services.GetRequiredService<IPublisher>();
                foreach (var message in published.Values.ToArray())
                    await publisher.PublishAsync(message, cancellationToken);

                await deliveries.WaitUntilProcessedAsync<IssueInvoice>(DeliveriesOfEachMessage, cancellationToken);
                await deliveries.WaitUntilProcessedAsync<SendInvoiceEmail>(DeliveriesOfEachMessage, cancellationToken);
                stored = await WaitForInvoiceAsync(run, correlationId, cancellationToken);
            },
            async run =>
            {
                // a copy is not a failure: it does not poison its queue
                await AssertNoDeadLettersAsync(run);

                // copies handled at the same time can find the saga locked, and nothing else
                Assert.All(run.ProcessingFailures, failure => Assert.True(failure.IsLockConflict, failure.ToString()));
            });

        // a second run of the first handler would publish a second email, under another id
        Assert.Equal(Messages, published.Count);
        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
        Assert.Equal(Messages, runs.Runs.Count);

        var state = Assert.IsType<InvoiceState>(stored!.StateData);
        Assert.Equal(1, state.Issued);
        Assert.Equal(1, state.EmailsSent);
    }

    private static Task<SagaInstanceInfo> WaitForInvoiceAsync(ScenarioRun run, string correlationId, CancellationToken cancellationToken)
        => run.WaitForSagaAsync<InvoiceSaga>(correlationId,
            saga => saga.ProcessedMessages.Count == Messages && !saga.IsLocked,
            cancellationToken);

    /// <summary>publishes every message several times at once, and keeps it for whoever wants to publish it again.</summary>
    private sealed class RepeatingPublisher(IPublisher inner, ConcurrentDictionary<string, MessageEnvelope> published) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        {
            published.TryAdd(message.MessageId, message);

            await Task.WhenAll(Enumerable.Range(0, CopiesAtOnce)
                .Select(_ => inner.PublishAsync(message, cancellationToken).AsTask()));
        }
    }
}
