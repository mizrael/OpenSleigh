using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Transport.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the topology of Sample2: an API that only publishes, and workers that do the work. The API is configured
/// like the workers, sagas included, and set to publish only: it must never be given a message.
/// </summary>
public class PublishOnlyApiScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const string Api = "api";
    private const int TransfersCount = 20;
    private const int MessagesHandledByAnOpenTransfer = 2;

    /// <summary>
    /// not subscribing is up to the transport, whatever the store: each transport runs it.
    /// </summary>
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq, Combinations.MongoDbKafka], HostsCounts = [2])]
    public async Task an_api_that_only_publishes_leaves_the_work_to_the_workers(string combination, int hostsCount)
    {
        var workload = new TransferWorkload(TransfersCount);
        var api = new ApiActivity();

        await RunScenarioAsync(combination,
            async (run, cancellationToken) =>
            {
                await run.StartHostsAsync(hostsCount, WorkerSetup(workload));
                var apiHost = await run.StartHostAsync(ApiSetup(api));

                await workload.RunToSettlementAsync(run, [apiHost], cancellationToken);
                await workload.AssertEachIsStoredAsync(run, settled: true, cancellationToken);
            },
            AssertOnlyLockConflictsAsync);

        workload.AssertEveryMessageWasHandledOnce();
        Assert.Equal(0, api.Deliveries.Count);
    }

    /// <summary>
    /// the API appends to the outbox and never relays: the workers find its messages when they poll the outbox,
    /// with nothing in their own process to tell them there is something to read.
    /// Reading the outbox is code of the store: every store runs it, and one combination has Kafka.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [2])]
    public async Task messages_appended_by_an_api_are_relayed_by_workers_that_poll_the_outbox(string combination, int hostsCount)
    {
        var workload = new TransferWorkload(TransfersCount);
        var api = new ApiActivity();
        var apiRelay = new PausableRelay(isPaused: true);

        await RunScenarioAsync(combination,
            async (run, cancellationToken) =>
            {
                await run.StartHostsAsync(hostsCount, WorkerSetup(workload));
                var apiHost = await run.StartHostAsync(ApiSetup(api, apiRelay));
                ScenarioHost[] publishers = [apiHost];

                // on its own, while the workers are idle: a relay that is busy reads the outbox again anyway
                var first = workload.Transfers[0];
                await workload.OpenAsync([first], publishers, cancellationToken);
                await run.WaitForReleasedSagaAsync<TransferSaga>(first.CorrelationId, MessagesHandledByAnOpenTransfer, cancellationToken);

                await workload.OpenAsync(workload.Transfers.Skip(1), publishers, cancellationToken);
                await workload.WaitUntilAllAreDebitedAsync(run, cancellationToken);
                await workload.SettleAllAsync(publishers, cancellationToken);
                await workload.WaitUntilAllAreSettledAsync(run, cancellationToken);

                await workload.AssertEachIsStoredAsync(run, settled: true, cancellationToken);
            },
            AssertOnlyLockConflictsAsync);

        workload.AssertEveryMessageWasHandledOnce();
        Assert.Equal(0, api.Deliveries.Count);
        Assert.Empty(api.PublishedTypes);
    }

    /// <summary>
    /// a host that only publishes has no consumer: a queue of its own would keep a copy of every message for nobody.
    /// The queues are declared by the transport, whatever the store.
    /// </summary>
    [Theory]
    [MatrixData(Only = [Combinations.PostgreSqlRabbitMq], HostsCounts = [1])]
    public async Task an_api_that_only_publishes_leaves_no_message_behind_on_rabbitmq(string combination, int hostsCount)
    {
        var workload = new TransferWorkload(TransfersCount);
        var api = new ApiActivity();
        IReadOnlyDictionary<string, uint> leftBehind = new Dictionary<string, uint>();

        await RunScenarioAsync(combination,
            async (run, cancellationToken) =>
            {
                await run.StartHostsAsync(hostsCount, WorkerSetup(workload));
                var apiHost = await run.StartHostAsync(ApiSetup(api));

                await workload.RunToSettlementAsync(run, [apiHost], cancellationToken);
                await workload.AssertEachIsStoredAsync(run, settled: true, cancellationToken);

                leftBehind = await ReadMessagesLeftInQueuesOfAsync(apiHost, api.PublishedTypes.Distinct(), cancellationToken);
                foreach (var (queueName, messagesCount) in leftBehind)
                    Console.WriteLine($"queue '{queueName}' has no consumer and holds {messagesCount} messages");
            },
            AssertOnlyLockConflictsAsync);

        Assert.Empty(leftBehind);
    }

    private static HostSetup WorkerSetup(TransferWorkload workload)
        => new(bus => bus.AddSaga<TransferSaga, TransferState>())
        {
            ConfigureServices = services => workload.AddTo(services)
        };

    private static HostSetup ApiSetup(ApiActivity activity, PausableRelay? relay = null)
        => new(bus => bus.AddSaga<TransferSaga, TransferState>().SetPublishOnly())
        {
            Service = Api,
            ConfigureServices = services =>
            {
                services
                    .ObserveDeliveriesOf<OpenTransfer>(activity.Deliveries)
                    .ObserveDeliveriesOf<DebitAccount>(activity.Deliveries)
                    .ObserveDeliveriesOf<SettleTransfer>(activity.Deliveries)
                    .Decorate<IPublisher>((_, inner) => new ObservedPublisher(inner, activity));

                if (relay is not null)
                    services.Intercept(_ => relay);
            }
        };

    // the queues the host declared for itself, one per message type it published, that still hold messages.
    // Asked on a channel of its own, on the connection of the host: its publish channel belongs to its relay
    private static async Task<IReadOnlyDictionary<string, uint>> ReadMessagesLeftInQueuesOfAsync(
        ScenarioHost host, IEnumerable<Type> messageTypes, CancellationToken cancellationToken)
    {
        var connection = host.Services.GetRequiredService<IBusConnection>();
        var queueReferences = host.Services.GetRequiredService<IQueueReferenceFactory>();

        var leftBehind = new Dictionary<string, uint>();
        foreach (var messageType in messageTypes)
        {
            var queueName = queueReferences.Create(messageType).QueueName;

            // a channel is closed by the broker when it asks for a queue that does not exist: one per question
            await using var channel = await connection.CreateChannelAsync(cancellationToken);
            try
            {
                var queue = await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
                if (queue.MessageCount > 0)
                    leftBehind[queueName] = queue.MessageCount;
            }
            catch (OperationInterruptedException e) when (e.ShutdownReason?.ReplyCode == Constants.NotFound)
            {
                // no queue, nothing left behind
            }
        }

        return leftBehind;
    }

    /// <summary>what the API did with the transport.</summary>
    private sealed class ApiActivity
    {
        private readonly ConcurrentQueue<Type> _publishedTypes = new();

        /// <summary>counts the messages the API was given, whatever it did with them.</summary>
        public Expectation Deliveries { get; } = new(1);

        /// <summary>the type of every message the API relayed, one entry per message.</summary>
        public IReadOnlyCollection<Type> PublishedTypes => _publishedTypes;

        public void MessagePublished(Type messageType) => _publishedTypes.Enqueue(messageType);
    }

    private sealed class ObservedPublisher(IPublisher inner, ApiActivity activity) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            await inner.PublishAsync(envelope, cancellationToken);
            activity.MessagePublished(envelope.MessageType);
        }
    }
}
