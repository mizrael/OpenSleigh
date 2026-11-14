using System.Collections.Concurrent;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// several hosts relay one outbox while it fills up: the messages a burst of requests appends, and the ones
/// the sagas publish while handling them.
/// </summary>
public class OutboxRelayUnderLoadScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // enough for every relay to find messages another relay is publishing, pass after pass:
    // 600 messages go through the outbox, and a relay takes 10 at a time on the SQL stores
    private const int TransfersCount = 200;
    private const int HostsCount = 3;

    // the relay is code of the store, which claims the messages of a pass and removes them: every store runs it,
    // on RabbitMQ. A transport only adds the publisher the relay calls: one combination has the one of Kafka
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [HostsCount])]
    public async Task every_message_is_relayed_once_by_hosts_that_share_an_outbox(string combination, int hostsCount)
    {
        var workload = new TransferWorkload(TransfersCount);
        var deliveries = new ConcurrentDictionary<string, int>();

        var setup = new HostSetup(bus => bus.AddSaga<TransferSaga, TransferState>())
        {
            ConfigureServices = services => workload.AddTo(services)
                .Decorate<IMessageProcessor>((_, inner) => new CountingProcessor(inner, deliveries))
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await workload.OpenAllAsync(run.Hosts, cancellationToken);
            await workload.Completed.Reached.WaitAsync(cancellationToken);
        });

        workload.AssertEachCompletedOnce();

        // a message relayed by two hosts is delivered twice. Its saga would ignore the copy: it is counted before that
        Assert.Equal(TransfersCount * TransferWorkload.MessagesPerTransfer, deliveries.Count);
        Assert.DoesNotContain(deliveries, delivery => delivery.Value != 1);
    }

    /// <summary>counts the messages its host was given and processed, by id. One that has to be retried is counted when it succeeds.</summary>
    private sealed class CountingProcessor(IMessageProcessor inner, ConcurrentDictionary<string, int> deliveries) : IMessageProcessor
    {
        public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            await inner.ProcessAsync(outboxMessage, cancellationToken);
            deliveries.AddOrUpdate(outboxMessage.MessageId, 1, (_, count) => count + 1);
        }
    }
}
