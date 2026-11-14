using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using RabbitMQ.Client;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// messages of one type, for one saga instance, published one after the other: in which order are they handled?
/// Only one case promises an order, and asserts it. The others assert what holds whatever the order (every message
/// handled once, none lost) and write the order they observed to the output, on a line starting with
/// <see cref="ObservedOrder"/>, for whoever wants to know how often it is kept.
/// </summary>
public class MessageOrderingScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    /// <summary>what a line of the output that reports the order of a run starts with.</summary>
    public const string ObservedOrder = "OBSERVED-ORDER";

    private const int ScansCount = 20;
    private const ushort SeveralMessagesAtATime = 4;

    private const string ThroughTheOutbox = "outbox";
    private const string StraightToTheBroker = "broker";
    private const string StraightToTheBrokerSeveralAtATime = "broker, 4 at a time";

    /// <summary>
    /// the one promise: a RabbitMQ queue with a single consumer that handles one message at a time, which is the default
    /// (<see cref="OpenSleigh.Transport.RabbitMQ.RabbitConfiguration.ConsumerDispatchConcurrency"/>), gives the messages
    /// in the order the broker got them. They are published straight to the broker here: the outbox promises no order.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq],
        HostsCounts = [1])]
    public async Task a_single_rabbitmq_consumer_handles_messages_in_the_order_the_broker_got_them(string combination, int hostsCount)
    {
        var handled = await ScanParcelAsync(combination, hostsCount, StraightToTheBroker, PublishToTheBrokerAsync);

        Assert.Equal(Enumerable.Range(1, ScansCount), handled);
    }

    /// <summary>
    /// the same consumer handling several messages at a time: they contend for the saga, the ones that lose
    /// are retried later, and the order is given up.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq],
        HostsCounts = [1])]
    public Task a_rabbitmq_consumer_that_handles_several_messages_at_a_time_handles_each_once(string combination, int hostsCount)
        => ScanParcelAsync(combination, hostsCount, StraightToTheBrokerSeveralAtATime, PublishToTheBrokerAsync,
            services => services.Decorate<IConnectionFactory>((_, connectionFactory) =>
            {
                ((ConnectionFactory)connectionFactory).ConsumerDispatchConcurrency = SeveralMessagesAtATime;
                return connectionFactory;
            }));

    /// <summary>
    /// Kafka keeps the order within a partition, and the messages of a type are spread over the partitions
    /// of its topic by message id: a single consumer reads them partition by partition.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerKafka, Combinations.PostgreSqlKafka, Combinations.MongoDbKafka],
        HostsCounts = [1])]
    public Task a_single_kafka_consumer_handles_each_message_once(string combination, int hostsCount)
        => ScanParcelAsync(combination, hostsCount, StraightToTheBroker, PublishToTheBrokerAsync);

    /// <summary>
    /// the way an application publishes: through the outbox, which is read in no particular order.
    /// </summary>
    [Theory]
    [MatrixData(HostsCounts = [1])]
    public Task messages_published_through_the_outbox_are_each_handled_once(string combination, int hostsCount)
        => ScanParcelAsync(combination, hostsCount, ThroughTheOutbox, PublishThroughTheOutboxAsync);

    /// <summary>
    /// the same with several hosts: a relay per host reads the outbox, and every host consumes.
    /// One combination per store, and both transports: the order is not kept on any of them.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlKafka, Combinations.MongoDbRabbitMq],
        HostsCounts = [3])]
    public Task messages_published_through_the_outbox_are_each_handled_once_by_several_hosts(string combination, int hostsCount)
        => ScanParcelAsync(combination, hostsCount, ThroughTheOutbox, PublishThroughTheOutboxAsync);

    private static Task PublishThroughTheOutboxAsync(ScenarioHost host, ParcelScanned scan, CancellationToken cancellationToken)
        => host.PublishAsync(scan, cancellationToken);

    /// <returns>the sequence numbers of the scans, in the order the saga handled them.</returns>
    private async Task<IReadOnlyList<int>> ScanParcelAsync(
        string combination,
        int hostsCount,
        string publishedThrough,
        Func<ScenarioHost, ParcelScanned, CancellationToken, Task> publishAsync,
        Action<IServiceCollection>? configureServices = null)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var scanned = new Expectation(ScansCount);
        Action<IMessageContext<ParcelScanned>> onScanned = _ => scanned.Signal();

        var setup = new HostSetup(bus => bus.AddSaga<ParcelSaga, ParcelState>())
        {
            ConfigureServices = services =>
            {
                services.AddSingleton(onScanned);
                configureServices?.Invoke(services);
            }
        };

        IReadOnlyList<int> handled = [];
        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            var publisher = run.AnyHost;

            // a scan that overtakes the message that starts its saga is dead-lettered, by design: the parcel is stored first
            await publisher.PublishAsync(new RegisterParcel(correlationId), cancellationToken);
            await run.WaitForSagaAsync<ParcelSaga>(correlationId,
                saga => saga.ProcessedMessages.Count == 1 && !saga.IsLocked, cancellationToken);

            for (var sequence = 1; sequence <= ScansCount; sequence++)
                await publishAsync(publisher, new ParcelScanned(correlationId, sequence), cancellationToken);

            await scanned.Reached.WaitAsync(cancellationToken);

            // the handler reports before its saga is stored
            var stored = await run.WaitForSagaAsync<ParcelSaga>(correlationId,
                saga => saga.ProcessedMessages.Count == ScansCount + 1 && !saga.IsLocked, cancellationToken);
            handled = Assert.IsType<ParcelState>(stored.StateData).Scans.ToArray();
        });

        var inOrder = handled.SequenceEqual(Enumerable.Range(1, ScansCount));
        Console.WriteLine(
            $"{ObservedOrder} | {combination} | hosts: {hostsCount} | through: {publishedThrough} | in order: {inOrder} | {string.Join(' ', handled)}");

        // also after the observation window: no scan was handled a second time, and none was lost
        Assert.Equal(ScansCount, scanned.Count);
        Assert.Equal(Enumerable.Range(1, ScansCount), handled.Order());

        return handled;
    }

    // what the outbox relay does with a message, without the outbox: the order the broker gets the messages in is this one
    private static async Task PublishToTheBrokerAsync(ScenarioHost host, ParcelScanned scan, CancellationToken cancellationToken)
    {
        var envelope = MessageEnvelope.Create(scan, host.Services.GetRequiredService<ISystemInfo>());
        await host.Services.GetRequiredService<IPublisher>().PublishAsync(envelope, cancellationToken);
    }
}
