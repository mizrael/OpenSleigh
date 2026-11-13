using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport.Kafka;

namespace OpenSleigh.E2ETests.Harness.Transports;

/// <summary>
/// a topic and a dead letter topic per message type, named after the run and created before the hosts start:
/// a consumer that subscribes to a topic that does not exist yet finds it only at its next metadata refresh.
/// The hosts of a service are one consumer group.
/// </summary>
internal sealed class KafkaTransport : ITransport
{
    private const string ConnectionStringName = "kafka";
    private const string DeadLetterSuffix = ".dead";
    private const int Partitions = 1;
    private const short ReplicationFactor = 1;
    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(10);

    private readonly string _runId;
    private readonly string _bootstrapServers;
    private readonly SemaphoreSlim _topicsGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _topics = new();
    private readonly ConcurrentDictionary<string, byte> _deadLetterTopics = new();

    public KafkaTransport(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _runId = context.RunId;
        _bootstrapServers = context.GetRequiredConnectionString(ConnectionStringName);
    }

    public void Configure(IBusConfigurator bus, string service)
        => bus.UseKafkaTransport(new KafkaConfiguration(_bootstrapServers, CreateQueueReferences));

    private QueueReferences CreateQueueReferences(Type messageType)
    {
        var topicName = $"{_runId}.{messageType.Name}";
        return new QueueReferences(topicName, topicName + DeadLetterSuffix);
    }

    public async Task PrepareAsync(IServiceProvider hostServices, CancellationToken cancellationToken)
    {
        await _topicsGate.WaitAsync(cancellationToken);
        try
        {
            var missing = new List<string>();
            foreach (var messageType in hostServices.GetHandledMessageTypes())
            {
                var references = CreateQueueReferences(messageType);
                if (_topics.TryAdd(references.TopicName, 0))
                    missing.Add(references.TopicName);
                if (_deadLetterTopics.TryAdd(references.DeadLetterTopicName, 0))
                    missing.Add(references.DeadLetterTopicName);
            }

            if (missing.Count > 0)
                await CreateTopicsAsync(missing);
        }
        finally
        {
            _topicsGate.Release();
        }
    }

    private async Task CreateTopicsAsync(IEnumerable<string> topicNames)
    {
        using var admin = CreateAdminClient();
        await admin.CreateTopicsAsync(
            topicNames.Select(name => new TopicSpecification
            {
                Name = name,
                NumPartitions = Partitions,
                ReplicationFactor = ReplicationFactor
            }),
            new CreateTopicsOptions { RequestTimeout = BrokerTimeout, OperationTimeout = BrokerTimeout });
    }

    public bool SupportsDeadLetters => true;

    public Task<long> CountDeadLettersAsync(CancellationToken cancellationToken)
        => Task.Run(CountDeadLetters, cancellationToken);

    private long CountDeadLetters()
    {
        // offsets are asked through a consumer, which is never subscribed to anything
        var config = new ConsumerConfig { BootstrapServers = _bootstrapServers, GroupId = $"{_runId}.probe" };
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(config).Build();

        long count = 0;
        foreach (var topicName in _deadLetterTopics.Keys)
        {
            for (var partition = 0; partition < Partitions; partition++)
            {
                var offsets = consumer.QueryWatermarkOffsets(new TopicPartition(topicName, partition), BrokerTimeout);
                count += offsets.High.Value - offsets.Low.Value;
            }
        }

        return count;
    }

    public async ValueTask DisposeAsync()
    {
        var topicNames = _topics.Keys.Concat(_deadLetterTopics.Keys).ToArray();
        if (topicNames.Length == 0)
            return;

        try
        {
            using var admin = CreateAdminClient();
            await admin.DeleteTopicsAsync(topicNames, new DeleteTopicsOptions { RequestTimeout = BrokerTimeout });
        }
        catch (KafkaException)
        {
            // what is left behind does not outlive the broker of the test run
        }
    }

    private IAdminClient CreateAdminClient()
        => new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
}
