using Microsoft.Extensions.Configuration;
using System;

namespace OpenSleigh.Transport.Kafka.Tests.Fixtures
{
    public class KafkaFixture
    {
        private readonly string _connStr;
        public KafkaFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            _connStr = configuration.GetConnectionString("kafka");
        }

        public KafkaConfiguration BuildKafkaConfiguration(string topicPrefix)
        {
            if(string.IsNullOrWhiteSpace(topicPrefix))
                topicPrefix = Guid.NewGuid().ToString();
            
            return new KafkaConfiguration(_connStr, 
                t => new QueueReferences($"{topicPrefix}.{t.FullName}", $"{topicPrefix}.{t.FullName}.dead"));
        }
    }
}