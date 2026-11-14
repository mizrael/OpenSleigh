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
/// like the workers, sagas included, and set to publish only: it must never handle a message.
/// </summary>
public class PublishOnlyApiScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const string Api = "api";
    private const int TransfersCount = 20;

    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [2])]
    public async Task an_api_that_only_publishes_leaves_the_work_to_the_workers(string combination, int hostsCount)
    {
        var workload = new TransferWorkload(TransfersCount);
        var apiActivity = new HostActivity();

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            await run.StartHostsAsync(hostsCount, WorkerSetup(workload));
            var api = await run.StartHostAsync(ApiSetup(apiActivity));

            await workload.OpenAllAsync([api], cancellationToken);
            await workload.Completed.Reached.WaitAsync(cancellationToken);

            await workload.AssertEachIsStoredAsCompletedAsync(run, cancellationToken);
        });

        workload.AssertEachCompletedOnce();
        Assert.Equal(0, apiActivity.ProcessedCount);
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
        var apiActivity = new HostActivity();
        var apiRelay = new PausableRelay(isPaused: true);

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            await run.StartHostsAsync(hostsCount, WorkerSetup(workload));
            var api = await run.StartHostAsync(ApiSetup(apiActivity, apiRelay));

            // on its own, while the workers are idle: a relay that is busy reads the outbox again anyway
            var first = workload.Transfers[0];
            await api.PublishAsync(first, cancellationToken);
            await run.WaitForSagaAsync<TransferSaga>(first.CorrelationId, saga => saga.IsCompleted, cancellationToken);

            await workload.OpenAsync(workload.Transfers.Skip(1).ToArray(), [api], cancellationToken);
            await workload.Completed.Reached.WaitAsync(cancellationToken);

            await workload.AssertEachIsStoredAsCompletedAsync(run, cancellationToken);
        });

        workload.AssertEachCompletedOnce();
        Assert.Equal(0, apiActivity.ProcessedCount);
        Assert.Empty(apiActivity.PublishedTypes);
    }

    /// <summary>
    /// a host that only publishes has no consumer: a queue of its own would keep a copy of every message for nobody.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq],
        HostsCounts = [1])]
    public async Task an_api_that_only_publishes_leaves_no_message_behind_on_rabbitmq(string combination, int hostsCount)
    {
        var workload = new TransferWorkload(TransfersCount);
        var apiActivity = new HostActivity();
        IReadOnlyDictionary<string, uint> leftBehind = new Dictionary<string, uint>();

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            await run.StartHostsAsync(hostsCount, WorkerSetup(workload));
            var api = await run.StartHostAsync(ApiSetup(apiActivity));

            await workload.OpenAllAsync([api], cancellationToken);
            await workload.Completed.Reached.WaitAsync(cancellationToken);
            await workload.AssertEachIsStoredAsCompletedAsync(run, cancellationToken);

            leftBehind = await ReadMessagesLeftInQueuesOfAsync(api, apiActivity.PublishedTypes.Distinct(), cancellationToken);
            foreach (var (queueName, messagesCount) in leftBehind)
                Console.WriteLine($"queue '{queueName}' has no consumer and holds {messagesCount} messages");
        });

        Assert.Empty(leftBehind);
    }

    private static HostSetup WorkerSetup(TransferWorkload workload)
        => new(bus => bus.AddSaga<TransferSaga, TransferState>())
        {
            ConfigureServices = services => workload.AddTo(services)
        };

    private static HostSetup ApiSetup(HostActivity activity, PausableRelay? relay = null)
        => new(bus => bus.AddSaga<TransferSaga, TransferState>().SetPublishOnly())
        {
            Service = Api,
            ConfigureServices = services =>
            {
                // never called, unless the API handles a message after all
                Action<IMessageContext<CreditAccount>> onCompleted = _ => { };

                services.AddSingleton(onCompleted)
                    .Decorate<IMessageProcessor>((_, inner) => new ObservedProcessor(inner, activity))
                    .Decorate<IPublisher>((_, inner) => new ObservedPublisher(inner, activity));

                if (relay is not null)
                    services.Intercept(_ => relay);
            }
        };

    // the queues the host declared for itself, one per message type it published, that still hold messages
    private static async Task<IReadOnlyDictionary<string, uint>> ReadMessagesLeftInQueuesOfAsync(
        ScenarioHost host, IEnumerable<Type> messageTypes, CancellationToken cancellationToken)
    {
        var configuration = host.Services.GetRequiredService<RabbitConfiguration>();
        var queueReferences = host.Services.GetRequiredService<IQueueReferenceFactory>();

        var connectionFactory = new ConnectionFactory
        {
            HostName = configuration.HostName,
            UserName = configuration.UserName,
            Password = configuration.Password,
            VirtualHost = configuration.VirtualHost,
        };
        await using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);

        var leftBehind = new Dictionary<string, uint>();
        foreach (var messageType in messageTypes)
        {
            var queueName = queueReferences.Create(messageType).QueueName;

            // a channel is closed by the broker when it asks for a queue that does not exist: one per question
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
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

    /// <summary>what a host did with the transport.</summary>
    private sealed class HostActivity
    {
        private readonly ConcurrentQueue<Type> _publishedTypes = new();
        private int _processedCount;

        public int ProcessedCount => Volatile.Read(ref _processedCount);

        /// <summary>the type of every message the host relayed, one entry per message.</summary>
        public IReadOnlyCollection<Type> PublishedTypes => _publishedTypes;

        public void MessageProcessed() => Interlocked.Increment(ref _processedCount);

        public void MessagePublished(Type messageType) => _publishedTypes.Enqueue(messageType);
    }

    private sealed class ObservedProcessor(IMessageProcessor inner, HostActivity activity) : IMessageProcessor
    {
        public ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            activity.MessageProcessed();
            return inner.ProcessAsync(outboxMessage, cancellationToken);
        }
    }

    private sealed class ObservedPublisher(IPublisher inner, HostActivity activity) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            await inner.PublishAsync(envelope, cancellationToken);
            activity.MessagePublished(envelope.MessageType);
        }
    }
}
