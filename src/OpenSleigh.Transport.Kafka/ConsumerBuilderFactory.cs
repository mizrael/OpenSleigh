using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka;

public class ConsumerBuilderFactory : IConsumerBuilderFactory
{
    private readonly KafkaConfiguration _kafkaConfiguration;
    private readonly ISystemInfo _sysInfo;

    public ConsumerBuilderFactory(
        KafkaConfiguration kafkaConfiguration, 
        ISystemInfo sysInfo)
    {
        _kafkaConfiguration = kafkaConfiguration ?? throw new ArgumentNullException(nameof(kafkaConfiguration));
        _sysInfo = sysInfo;
    }

    public ConsumerBuilder<TKey, TValue> Create<TKey, TValue>()
    {       
        var config = new ConsumerConfig()
        {
            GroupId = _sysInfo.ClientGroup,
            BootstrapServers = _kafkaConfiguration.ConnectionString,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = true,
            // the subscriber stores the offset of a message once it has dealt with it, so that
            // a message is not skipped when the process dies, or while it waits to be processed again
            EnableAutoOffsetStore = false,
            // the retry topics only receive messages after a lock conflict, and a consumer notices
            // a topic created after it subscribed only at the next metadata refresh, minutes later
            AllowAutoCreateTopics = true
        };
        
        var builder = new ConsumerBuilder<TKey, TValue>(config);
        
        if(typeof(TKey) == typeof(Guid))
            (builder as ConsumerBuilder<Guid, TValue>).SetKeyDeserializer(new GuidDeserializer());

        return builder;
    }
}