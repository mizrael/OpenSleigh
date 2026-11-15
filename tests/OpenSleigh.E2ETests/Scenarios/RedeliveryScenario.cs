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
/// published several times in a row, and then again when the saga is long done with it.
/// </summary>
public class RedeliveryScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int CopiesInARow = 3;
    private const int DeliveriesOfEachMessage = 2 * CopiesInARow;
    private const int Messages = 2;

    // ignoring a copy is up to the store, and where the copies go is up to the transport: every store runs with one
    // transport and one store with the other. One host of these transports shows nothing that five hosts do not
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [5])]
    public Task a_message_delivered_again_is_not_handled_again_by_any_host(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_message_delivered_again_is_not_handled_again_by_a_host_that_handles_several_at_once(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    private async Task RunAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns();
        var batch = new InMemoryBatch();
        var receiptsProcessed = new Expectation(DeliveriesOfEachMessage);
        var emailsProcessed = new Expectation(DeliveriesOfEachMessage);
        var published = new ConcurrentDictionary<string, MessageEnvelope>();

        var setup = new HostSetup(bus => bus.AddSaga<ReceiptSaga, ReceiptState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .ReadInMemoryMessagesIn(batch)
                .ObserveDeliveriesOf<IssueReceipt>(receiptsProcessed)
                .ObserveDeliveriesOf<SendReceiptEmail>(emailsProcessed)
                .Decorate<IPublisher>((_, inner) => new RepeatingPublisher(inner, published))
        };

        // RabbitMQ gives the copies of a message to different hosts, and an in-memory host reads them together:
        // they are handled at the same time. On Kafka they follow each other on the partition of their message,
        // to one host: a copy is only handled once the one before it is done with
        var copiesAreHandledAtTheSameTime = !Combinations.Uses(combination, Combinations.Kafka);

        SagaInstanceInfo? stored = null;

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                Task PublishAsync() => run.AnyHost.PublishNewAsync(new IssueReceipt(correlationId), cancellationToken);

                if (copiesAreHandledAtTheSameTime)
                {
                    using (batch.Of(CopiesInARow))
                        await run.ForceLockConflictAsync(runs, _ => true, PublishAsync, cancellationToken);
                }
                else
                {
                    await PublishAsync();
                }

                await run.WaitForReleasedSagaAsync<ReceiptSaga>(correlationId, Messages, cancellationToken);

                // the messages come back when the saga has handled them and moved on, through another host when there is one
                var publisher = run.Hosts[^1].Services.GetRequiredService<IPublisher>();
                foreach (var message in published.Values.ToArray())
                    await publisher.PublishAsync(message, cancellationToken);

                await run.WaitForAsync($"the {DeliveriesOfEachMessage} copies of the receipt to be processed", receiptsProcessed.Reached, cancellationToken);
                await run.WaitForAsync($"the {DeliveriesOfEachMessage} copies of the email to be processed", emailsProcessed.Reached, cancellationToken);
                stored = await run.WaitForReleasedSagaAsync<ReceiptSaga>(correlationId, Messages, cancellationToken);
            },
            // a copy is not a failure and does not poison its queue. Copies handled at the same time can find the saga locked
            AssertOnlyLockConflictsAsync);

        // a second run of the first handler would publish a second email, under another id
        Assert.Equal(Messages, published.Count);
        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
        Assert.Equal(Messages, runs.Runs.Count);

        var state = Assert.IsType<ReceiptState>(stored!.StateData);
        Assert.Equal(1, state.Issued);
        Assert.Equal(1, state.EmailsSent);
    }

    /// <summary>
    /// publishes every message several times, one after the other as a relay does,
    /// and keeps it for whoever wants to publish it again.
    /// </summary>
    private sealed class RepeatingPublisher(IPublisher inner, ConcurrentDictionary<string, MessageEnvelope> published) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        {
            published.TryAdd(message.MessageId, message);

            for (var copy = 0; copy < CopiesInARow; copy++)
                await inner.PublishAsync(message, cancellationToken);
        }
    }
}
