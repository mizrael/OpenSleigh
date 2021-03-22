using Microsoft.Extensions.Configuration;
using System;

namespace OpenSleigh.Transport.Kafka.Tests.Fixtures
{
    public class KafkaFixture 
    {
        public KafkaFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var executionId = Guid.NewGuid();

            var connStr = configuration.GetConnectionString("kafka");
            var kafkaSection = configuration.GetSection("Kafka");
            var consumerGroup = $"{kafkaSection["ConsumerGroup"]}.{executionId}";

            this.KafkaConfiguration = new KafkaConfiguration(connStr, consumerGroup, t => {
                return new QueueReferences($"{executionId}.{t.FullName}");
            });
        }

        public KafkaConfiguration KafkaConfiguration { get; init; }
    }
}