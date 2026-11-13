using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport.Kafka;

namespace OpenSleigh.E2ETests.Harness.Transports;

/// <summary>
/// a topic and a dead letter topic per message type, named after the run and created before the hosts start:
/// a consumer that subscribes to a topic that does not exist yet finds it only at its next metadata refresh.
/// The hosts of a service are one consumer group, and a topic has enough partitions for each of them to get some:
/// with a single partition one host would consume everything and the others would only watch.
/// </summary>
internal sealed class KafkaTransport : ITransport
{
    /// <summary>how many hosts of one service can consume at the same time.</summary>
    public const int Partitions = 8;

    private const int DeadLetterPartitions = 1;
    private const string ConnectionStringName = "kafka";
    private const string DeadLetterSuffix = ".dead";
    private const short ReplicationFactor = 1;
    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AssignmentPollingInterval = TimeSpan.FromMilliseconds(100);

    private readonly string _runId;
    private readonly string _bootstrapServers;
    private readonly SemaphoreSlim _topicsGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _topics = new();
    private readonly ConcurrentDictionary<string, Type> _deadLetterTopics = new();

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
            var missing = new List<TopicSpecification>();
            foreach (var messageType in hostServices.GetHandledMessageTypes())
            {
                var references = CreateQueueReferences(messageType);
                if (_topics.TryAdd(references.TopicName, 0))
                    missing.Add(Topic(references.TopicName, Partitions));
                if (_deadLetterTopics.TryAdd(references.DeadLetterTopicName, messageType))
                    missing.Add(Topic(references.DeadLetterTopicName, DeadLetterPartitions));
            }

            if (missing.Count == 0)
                return;

            using var admin = CreateAdminClient();
            await admin.CreateTopicsAsync(missing,
                new CreateTopicsOptions { RequestTimeout = BrokerTimeout, OperationTimeout = BrokerTimeout });
        }
        finally
        {
            _topicsGate.Release();
        }
    }

    private static TopicSpecification Topic(string name, int partitions)
        => new() { Name = name, NumPartitions = partitions, ReplicationFactor = ReplicationFactor };

    // a host that has not been given its partitions yet consumes nothing: the scenario would run on the hosts
    // that joined first. What is published meanwhile is not lost, a new group reads its topics from the beginning
    public async Task WaitUntilSubscribedAsync(IReadOnlyCollection<IServiceProvider> runningHosts, CancellationToken cancellationToken)
    {
        var expectedMembers = runningHosts
            .Where(host => host.Consumes())
            .GroupBy(host => host.GetRequiredService<ISystemInfo>().ClientGroup)
            .ToDictionary(group => group.Key, group => group.Count());

        if (expectedMembers.Count == 0)
            return;

        var crowded = expectedMembers.FirstOrDefault(group => group.Value > Partitions);
        if (crowded.Key is not null)
            throw new InvalidOperationException(
                $"'{crowded.Key}' has {crowded.Value} hosts and its topics have {Partitions} partitions: some hosts would consume nothing.");

        using var admin = CreateAdminClient();
        while (!await EveryMemberHasPartitionsAsync(admin, expectedMembers))
            await Task.Delay(AssignmentPollingInterval, cancellationToken);
    }

    private static async Task<bool> EveryMemberHasPartitionsAsync(IAdminClient admin, IReadOnlyDictionary<string, int> expectedMembers)
    {
        DescribeConsumerGroupsResult groups;
        try
        {
            groups = await admin.DescribeConsumerGroupsAsync(expectedMembers.Keys,
                new DescribeConsumerGroupsOptions { RequestTimeout = BrokerTimeout });
        }
        catch (DescribeConsumerGroupsException)
        {
            // a group does not exist before its first member has joined
            return false;
        }

        return groups.ConsumerGroupDescriptions.All(group =>
            group.State == ConsumerGroupState.Stable
            && group.Members.Count == expectedMembers[group.GroupId]
            && group.Members.All(member => member.Assignment.TopicPartitions.Count > 0));
    }

    public bool SupportsDeadLetters => true;

    public Task<IReadOnlyDictionary<Type, long>> ReadDeadLettersAsync(CancellationToken cancellationToken)
        => Task.Run(ReadDeadLetters, cancellationToken);

    private IReadOnlyDictionary<Type, long> ReadDeadLetters()
    {
        // offsets are asked through a consumer, which is never subscribed to anything
        var config = new ConsumerConfig { BootstrapServers = _bootstrapServers, GroupId = $"{_runId}.probe" };
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(config).Build();

        var deadLetters = new Dictionary<Type, long>();
        foreach (var (topicName, messageType) in _deadLetterTopics)
        {
            long count = 0;
            for (var partition = 0; partition < DeadLetterPartitions; partition++)
            {
                var offsets = consumer.QueryWatermarkOffsets(new TopicPartition(topicName, partition), BrokerTimeout);
                count += offsets.High.Value - offsets.Low.Value;
            }

            if (count > 0)
                deadLetters[messageType] = count;
        }

        return deadLetters;
    }

    public async ValueTask DisposeAsync()
    {
        var topicNames = _topics.Keys.Concat(_deadLetterTopics.Keys).ToArray();
        if (topicNames.Length == 0)
            return;

        using var admin = CreateAdminClient();
        await admin.DeleteTopicsAsync(topicNames, new DeleteTopicsOptions { RequestTimeout = BrokerTimeout });
    }

    private IAdminClient CreateAdminClient()
        => new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
}
