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
            EnablePartitionEof = true
        };
        
        var builder = new ConsumerBuilder<TKey, TValue>(config);
        
        if(typeof(TKey) == typeof(Guid))
            (builder as ConsumerBuilder<Guid, TValue>).SetKeyDeserializer(new GuidDeserializer());

        return builder;
    }
}