using System;
using Confluent.Kafka;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.Kafka
{
    public class ConsumerBuilderFactory : IConsumerBuilderFactory
    {
        private readonly IGroupIdFactory _groupIdFactory;
        private readonly KafkaConfiguration _kafkaConfiguration;
        
        public ConsumerBuilderFactory(IGroupIdFactory groupIdFactory, KafkaConfiguration kafkaConfiguration)
        {
            _groupIdFactory = groupIdFactory ?? throw new ArgumentNullException(nameof(groupIdFactory));
            _kafkaConfiguration = kafkaConfiguration ?? throw new ArgumentNullException(nameof(kafkaConfiguration));
        }

        public ConsumerBuilder<TKey, TValue> Create<TM, TKey, TValue>() where TM : IMessage
        {
            var groupId = _groupIdFactory.Create<TM>();
            
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