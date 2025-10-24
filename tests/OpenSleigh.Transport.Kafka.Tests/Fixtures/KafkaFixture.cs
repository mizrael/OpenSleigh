using Microsoft.Extensions.Configuration;
using System;

namespace OpenSleigh.Transport.Kafka.Tests.Fixtures;

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

    public KafkaConfiguration BuildKafkaConfiguration(string? topicPrefix = null)
    {
        if(string.IsNullOrWhiteSpace(topicPrefix))
#if NET9_0_OR_GREATER
            topicPrefix = Guid.CreateVersion7().ToString();
#else
            topicPrefix = Guid.NewGuid().ToString();
#endif
        
        return new KafkaConfiguration(_connStr, 
            t => new QueueReferences($"{topicPrefix}.{t.FullName}", $"{topicPrefix}.{t.FullName}.dead"));
    }
}