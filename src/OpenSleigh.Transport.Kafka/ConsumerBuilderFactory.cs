using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka
{
    public class ConsumerBuilderFactory : IConsumerBuilderFactory
    {
        private readonly KafkaConfiguration _kafkaConfiguration;
        
        public ConsumerBuilderFactory(KafkaConfiguration kafkaConfiguration)
        {
            _kafkaConfiguration = kafkaConfiguration ?? throw new ArgumentNullException(nameof(kafkaConfiguration));
        }

        public ConsumerBuilder<TKey, TValue> Create<TM, TKey, TValue>() where TM : IMessage
        {
            var groupId = typeof(TM).FullName;
            
            var config = new ConsumerConfig()
            {
                GroupId = groupId,
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
}